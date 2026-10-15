using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Creates the next game of a recurring group once a game's summary is generated (DECISIONS #64),
/// so the group keeps playing and its ranking keeps adding up. Everyone who had confirmed the
/// finished game is put on the new one as Pending (they confirm again each game, and a private
/// game stays visible to them).
/// </summary>
public sealed class ScheduleNextGameHandler
{
    private readonly IMatchRepository _matches;
    private readonly IPresenceRepository _presences;
    private readonly IEventPublisher _eventPublisher;
    private readonly MatchWindowSynchronizer _windowSynchronizer;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ScheduleNextGameHandler> _logger;

    public ScheduleNextGameHandler(
        IMatchRepository matches,
        IPresenceRepository presences,
        IEventPublisher eventPublisher,
        MatchWindowSynchronizer windowSynchronizer,
        TimeProvider timeProvider,
        ILogger<ScheduleNextGameHandler> logger)
    {
        _matches = matches;
        _presences = presences;
        _eventPublisher = eventPublisher;
        _windowSynchronizer = windowSynchronizer;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>Schedules the game after <paramref name="finished"/>; does nothing for a one-off.</summary>
    public async Task ScheduleAsync(Match finished, CancellationToken cancellationToken)
    {
        if (finished.Type != Entities.MatchType.Recurring)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var next = finished.CreateNext(
            Match.NextGameTime(finished.DateTime, finished.Frequency, finished.RecurrenceDays, now),
            now);
        await _matches.AddAsync(next, cancellationToken);

        var confirmed = (await _presences.ListByMatchAsync(finished.Id, cancellationToken))
            .Where(p => p.Status == PresenceStatus.Confirmed);
        foreach (var presence in confirmed)
        {
            await _presences.AddAsync(MatchPresence.Create(next.Id, presence.PlayerId, presence.PlayerType, now), cancellationToken);
        }

        await _eventPublisher.PublishAsync(
            new MatchCreated(
                MatchId: next.Id,
                OrganizerId: next.OrganizerId,
                Name: next.Name,
                Type: next.Type.ToString(),
                DateTime: next.DateTime,
                WindowOpensAt: next.WindowOpensAt,
                WindowClosesAt: next.WindowClosesAt,
                MaxPlayers: next.MaxPlayers,
                RegularSlots: next.RegularSlots,
                DropInSlots: next.DropInSlots,
                OccurredAt: now),
            cancellationToken);

        await _windowSynchronizer.SyncAsync(next, cancellationToken);

        _logger.LogInformation(
            "Next recurring game scheduled. GroupId={GroupId} PreviousMatchId={PreviousMatchId} MatchId={MatchId} DateTime={DateTime}",
            next.GroupId, finished.Id, next.Id, next.DateTime);
    }
}
