using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Transitions a match from <c>Open</c> to <c>Closed</c>, computes released DropIn slots,
/// promotes waiting DropIn players, and publishes <see cref="MatchWindowClosed"/>.
/// Called by the Background Worker when <c>WindowClosesAt</c> is reached.
/// </summary>
public sealed class CloseMatchWindowHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CloseMatchWindowHandler> _logger;

    public CloseMatchWindowHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IWaitingListRepository waitingListRepository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        ILogger<CloseMatchWindowHandler> logger)
    {
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _waitingListRepository = waitingListRepository;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        var now = _timeProvider.GetUtcNow();

        // Compute how many Regular slots were left unconfirmed.
        var confirmedRegulars = await _presenceRepository.CountConfirmedAsync(
            matchId, PlayerType.Regular, cancellationToken);

        var releasedDropInSlots = Math.Max(0, match.RegularSlots - confirmedRegulars);

        // Throws InvalidMatchStatusTransitionException if not Open.
        match.CloseWindow(releasedDropInSlots, now);

        await _matchRepository.UpdateAsync(match, cancellationToken);

        _logger.LogInformation(
            "Match window closed. MatchId={MatchId} Status={Status} ReleasedDropInSlots={ReleasedDropInSlots}",
            matchId, match.Status, releasedDropInSlots);

        // Promote waiting DropIn players up to the number of released slots.
        if (releasedDropInSlots > 0)
        {
            var waiters = await _waitingListRepository.ListByMatchAsync(matchId, cancellationToken);
            var dropInWaiters = waiters
                .Where(w => w.PlayerType == PlayerType.DropIn)
                .Take(releasedDropInSlots)
                .ToList();

            foreach (var waiter in dropInWaiters)
            {
                var promotedPresence = MatchPresence.Create(matchId, waiter.PlayerId, PlayerType.DropIn, now);
                await _presenceRepository.AddAsync(promotedPresence, cancellationToken);
                await _waitingListRepository.RemoveAsync(waiter, cancellationToken);

                _logger.LogInformation(
                    "DropIn waiter promoted at window close. MatchId={MatchId} PlayerId={PlayerId} PlayerType={PlayerType} Status={Status}",
                    matchId, waiter.PlayerId, PlayerType.DropIn, promotedPresence.Status);
            }
        }

        var @event = new MatchWindowClosed(
            MatchId: match.Id,
            OrganizerId: match.OrganizerId,
            ReleasedDropInSlots: releasedDropInSlots,
            OccurredAt: now);

        await _eventPublisher.PublishAsync(@event, cancellationToken);
    }
}
