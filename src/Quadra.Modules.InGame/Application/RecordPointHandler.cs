using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.InGame;
using Quadra.Shared.Realtime;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Records a single point for one team in the current set, auto-finishing the set and the game
/// according to the volleyball best-of-N rules.
/// </summary>
public sealed class RecordPointHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IScoreboardRepository _scoreboardRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMatchRoomNotifier _matchRoomNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecordPointHandler> _logger;

    public RecordPointHandler(
        IMatchReader matchReader,
        IScoreboardRepository scoreboardRepository,
        IEventPublisher eventPublisher,
        IMatchRoomNotifier matchRoomNotifier,
        TimeProvider timeProvider,
        ILogger<RecordPointHandler> logger)
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
        int setNumber,
        Guid scoringTeamId,
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

        // 3. Load scoreboard (with sets).
        var scoreboard = await _scoreboardRepository.FindByMatchAsync(matchId, cancellationToken)
            ?? throw new ScoreboardNotFoundException();

        // 4. Must be in progress.
        if (scoreboard.State != ScoreboardState.InProgress)
        {
            throw new InvalidScoreboardStateException(
                "Points can only be recorded while the game is in progress.");
        }

        // 5. Scoring team must belong to this scoreboard.
        if (scoringTeamId != scoreboard.TeamAId && scoringTeamId != scoreboard.TeamBId)
        {
            throw new TeamNotInScoreboardException();
        }

        // 6. The set must be the current set.
        if (setNumber != scoreboard.CurrentSetNumber)
        {
            throw new SetNotCurrentException();
        }

        // 7. Locate the current set; it must not be finished.
        var currentSet = scoreboard.Sets.First(s => s.SetNumber == scoreboard.CurrentSetNumber);
        if (currentSet.Status == SetStatus.Finished)
        {
            throw new InvalidScoreboardStateException("This set is already finished.");
        }

        var now = _timeProvider.GetUtcNow();

        // 8. Record the point.
        currentSet.AddPoint(scoringTeamId, scoreboard.TeamAId, scoreboard.TeamBId);

        _logger.LogDebug(
            "Point recorded. MatchId={MatchId}, SetNumber={SetNumber}, ScoringTeamId={ScoringTeamId}, "
            + "TeamAPoints={TeamAPoints}, TeamBPoints={TeamBPoints}.",
            matchId, currentSet.SetNumber, scoringTeamId, currentSet.TeamAPoints, currentSet.TeamBPoints);

        // 9. Evaluate the set.
        var gameEnded = false;
        var target = VolleyballScoringRules.TargetForSet(scoreboard.Format, currentSet.SetNumber);
        if (VolleyballScoringRules.IsSetWon(currentSet.TeamAPoints, currentSet.TeamBPoints, target))
        {
            var setWinnerTeamId = currentSet.TeamAPoints > currentSet.TeamBPoints
                ? scoreboard.TeamAId
                : scoreboard.TeamBId;

            // 10. Close the set: the game ends, the next set opens, or (teams rotating) the
            //     organizer picks who plays next.
            gameEnded = ScoreboardProgression.CompleteSet(scoreboard, currentSet, setWinnerTeamId, now);
        }

        // 11. Persist all changes inside a single transaction.
        await _scoreboardRepository.UpdateAsync(scoreboard, cancellationToken);

        // 12. If the game ended, publish MatchEnded after commit.
        if (gameEnded)
        {
            _logger.LogInformation(
                "Game ended for MatchId={MatchId}. WinnerTeamId={WinnerTeamId}, "
                + "TeamASetsWon={TeamASetsWon}, TeamBSetsWon={TeamBSetsWon}.",
                matchId, scoreboard.WinnerTeamId, scoreboard.TeamASetsWon, scoreboard.TeamBSetsWon);

            await _eventPublisher.PublishAsync(BuildMatchEndedEvent(scoreboard, now), cancellationToken);
        }

        // 13. Broadcast to the SignalR match room.
        var message = GetScoreboardHandler.ToRealtimeMessage(scoreboard, now);
        await _matchRoomNotifier.NotifyScoreboardUpdatedAsync(message, cancellationToken);

        // 14. Map and return.
        return GetScoreboardHandler.MapToResponse(scoreboard);
    }

    internal static MatchEnded BuildMatchEndedEvent(Scoreboard scoreboard, DateTimeOffset occurredAt)
    {
        return new MatchEnded(
            MatchId: scoreboard.MatchId,
            TeamAId: scoreboard.TeamAId,
            TeamBId: scoreboard.TeamBId,
            TeamASetsWon: scoreboard.TeamASetsWon,
            TeamBSetsWon: scoreboard.TeamBSetsWon,
            WinnerTeamId: scoreboard.WinnerTeamId,
            Sets: scoreboard.Sets
                .OrderBy(s => s.SetNumber)
                .Select(s => new MatchEndedSetResult(
                    SetNumber: s.SetNumber,
                    TeamAPoints: s.TeamAPoints,
                    TeamBPoints: s.TeamBPoints,
                    WinnerTeamId: s.WinnerTeamId))
                .ToList(),
            EndedAt: scoreboard.EndedAt!.Value,
            OccurredAt: occurredAt);
    }
}
