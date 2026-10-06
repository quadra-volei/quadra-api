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
    private readonly IEventPublisher _eventPublisher;
    private readonly IMatchRoomNotifier _matchRoomNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<EndScoreboardHandler> _logger;

    public EndScoreboardHandler(
        IMatchReader matchReader,
        IScoreboardRepository scoreboardRepository,
        IEventPublisher eventPublisher,
        IMatchRoomNotifier matchRoomNotifier,
        TimeProvider timeProvider,
        ILogger<EndScoreboardHandler> logger)
    {
        _matchReader = matchReader;
        _scoreboardRepository = scoreboardRepository;
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

        // 5. Winner derived from sets won so far; null if equal.
        Guid? winnerTeamId = scoreboard.TeamASetsWon > scoreboard.TeamBSetsWon
            ? scoreboard.TeamAId
            : scoreboard.TeamBSetsWon > scoreboard.TeamASetsWon
                ? scoreboard.TeamBId
                : null;

        // 6. Mark the current unfinished set (if any) as abandoned.
        var currentSet = scoreboard.Sets
            .FirstOrDefault(s => s.SetNumber == scoreboard.CurrentSetNumber && s.Status == SetStatus.InProgress);
        currentSet?.Finish(winnerTeamId: null, now);

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
