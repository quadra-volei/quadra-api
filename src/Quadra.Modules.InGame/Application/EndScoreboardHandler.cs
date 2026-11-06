using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Realtime;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Force-ends an in-progress game early; the winner is derived from the sets won so far
/// (<c>null</c> when equal).
/// </summary>
public sealed class EndScoreboardHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IScoreboardRepository _scoreboardRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly IConfirmedPlayersReader _confirmedPlayers;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMatchRoomNotifier _matchRoomNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EndScoreboardHandler> _logger;

    public EndScoreboardHandler(
        IMatchReader matchReader,
        IScoreboardRepository scoreboardRepository,
        ITeamRepository teamRepository,
        IConfirmedPlayersReader confirmedPlayers,
        IEventPublisher eventPublisher,
        IMatchRoomNotifier matchRoomNotifier,
        TimeProvider timeProvider,
        ILogger<EndScoreboardHandler> logger)
    {
        _matchReader = matchReader;
        _scoreboardRepository = scoreboardRepository;
        _teamRepository = teamRepository;
        _confirmedPlayers = confirmedPlayers;
        _eventPublisher = eventPublisher;
        _matchRoomNotifier = matchRoomNotifier;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ScoreboardResponse> HandleAsync(
        Guid matchId,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        // 1. Verify match exists.
        var matchSummary = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // 2. Verify caller is organizer.
        if (matchSummary.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        // 3. Load scoreboard.
        var scoreboard = await _scoreboardRepository.FindByMatchAsync(matchId, cancellationToken)
            ?? throw new ScoreboardNotFoundException();

        // 4. Must be in progress.
        if (scoreboard.State != ScoreboardState.InProgress)
        {
            throw new InvalidScoreboardStateException("Only an in-progress game can be ended.");
        }

        var now = _timeProvider.GetUtcNow();

        // 5. The game on court when the organizer ends the match goes to whoever leads it (as
        //    ending a set early does); only a tied one is left without a winner. Before, every
        //    unfinished game ended without a winner and the summary showed "Empate 17-15".
        var currentSet = scoreboard.Sets
            .FirstOrDefault(s => s.SetNumber == scoreboard.CurrentSetNumber && s.Status == SetStatus.InProgress);
        if (currentSet is not null
            && scoreboard.Format != ScoreboardFormat.Free
            && currentSet.TeamAPoints != currentSet.TeamBPoints)
        {
            var leader = currentSet.TeamAPoints > currentSet.TeamBPoints ? scoreboard.TeamAId : scoreboard.TeamBId;
            currentSet.Finish(leader, now);
            scoreboard.RegisterSetWon(leader);
        }
        else
        {
            currentSet?.Finish(winnerTeamId: null, now);
        }

        // 6. Winner derived from sets won, the one just closed included; null if equal.
        Guid? winnerTeamId = scoreboard.RotatesTeams
            ? scoreboard.LeaderBySets()
            : scoreboard.TeamASetsWon > scoreboard.TeamBSetsWon
                ? scoreboard.TeamAId
                : scoreboard.TeamBSetsWon > scoreboard.TeamASetsWon
                    ? scoreboard.TeamBId
                    : null;

        // 6b. No fixed teams: whoever confirmed during the session also played.
        if (scoreboard.Format == ScoreboardFormat.Free)
        {
            var roster = await FreeRoster.ReplaceAsync(matchId, _confirmedPlayers, _teamRepository, now, cancellationToken);
            scoreboard.SetPair(roster.Id, roster.Id);
        }

        // 7. End the game.
        scoreboard.End(winnerTeamId, now);

        // 8. Persist inside a single transaction.
        await _scoreboardRepository.UpdateAsync(scoreboard, cancellationToken);

        _logger.LogInformation(
            "Game force-ended for MatchId={MatchId}. WinnerTeamId={WinnerTeamId}, "
            + "TeamASetsWon={TeamASetsWon}, TeamBSetsWon={TeamBSetsWon}.",
            matchId, scoreboard.WinnerTeamId, scoreboard.TeamASetsWon, scoreboard.TeamBSetsWon);

        // 9. Publish MatchEnded after commit.
        await _eventPublisher.PublishAsync(
            RecordPointHandler.BuildMatchEndedEvent(scoreboard, now), cancellationToken);

        // 10. Broadcast to the SignalR match room.
        var message = GetScoreboardHandler.ToRealtimeMessage(scoreboard, now);
        await _matchRoomNotifier.NotifyScoreboardUpdatedAsync(message, cancellationToken);

        // 11. Map and return.
        return GetScoreboardHandler.MapToResponse(scoreboard);
    }
}
