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
/// Transitions a scoreboard from <see cref="ScoreboardState.NotStarted"/> to
/// <see cref="ScoreboardState.InProgress"/> and opens set 1.
/// </summary>
public sealed class StartScoreboardHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IScoreboardRepository _scoreboardRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMatchRoomNotifier _matchRoomNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<StartScoreboardHandler> _logger;

    public StartScoreboardHandler(
        IMatchReader matchReader,
        IScoreboardRepository scoreboardRepository,
        IEventPublisher eventPublisher,
        IMatchRoomNotifier matchRoomNotifier,
        TimeProvider timeProvider,
        ILogger<StartScoreboardHandler> logger)
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

        // 4. Must be NotStarted.
        if (scoreboard.State != ScoreboardState.NotStarted)
        {
            throw new InvalidScoreboardStateException("The game has already started or ended.");
        }

        // 5. Transition to InProgress and open set 1.
        var now = _timeProvider.GetUtcNow();
        scoreboard.Start(now);
        scoreboard.OpenSet(setNumber: 1, isDecidingSet: false, now);

        // 6. Persist inside a single transaction.
        await _scoreboardRepository.UpdateAsync(scoreboard, cancellationToken);

        _logger.LogInformation(
            "Scoreboard started for MatchId={MatchId}. Format={Format}, State={State}.",
            matchId, scoreboard.Format, scoreboard.State);

        // 7. Publish MatchStarted after commit.
        var startedEvent = new MatchStarted(
            MatchId: scoreboard.MatchId,
            TeamAId: scoreboard.TeamAId,
            TeamBId: scoreboard.TeamBId,
            Format: scoreboard.Format.ToString(),
            StartedAt: scoreboard.StartedAt!.Value,
            OccurredAt: now);
        await _eventPublisher.PublishAsync(startedEvent, cancellationToken);

        // 8. Broadcast to the SignalR match room.
        var message = GetScoreboardHandler.ToRealtimeMessage(scoreboard, now);
        await _matchRoomNotifier.NotifyScoreboardUpdatedAsync(message, cancellationToken);

        // 9. Map and return.
        return GetScoreboardHandler.MapToResponse(scoreboard);
    }
}
