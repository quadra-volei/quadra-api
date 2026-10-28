using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;
using Quadra.Shared.Realtime;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Orchestrates removing a player's presence from a match (DELETE /api/v1/matches/{id}/presences/{playerId}).
/// Only the organizer may remove players, any time before the game starts (Draft, Open or
/// Closed). The slot a confirmed player leaves goes to the first one waiting for that kind of
/// slot, as when someone declines. The organizer cannot remove themselves.
/// </summary>
public sealed class RemovePresenceHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMatchRoomNotifier _roomNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RemovePresenceHandler> _logger;

    public RemovePresenceHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IWaitingListRepository waitingListRepository,
        IEventPublisher eventPublisher,
        IMatchRoomNotifier roomNotifier,
        TimeProvider timeProvider,
        ILogger<RemovePresenceHandler> logger)
    {
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _waitingListRepository = waitingListRepository;
        _eventPublisher = eventPublisher;
        _roomNotifier = roomNotifier;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(
        Guid matchId,
        Guid callerId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        // Same lock as a confirmation: the freed slot and the promotion are counted once.
        await using var transaction = await _matchRepository.LockAsync(matchId, cancellationToken);

        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        if (match.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        if (match.Status is not (MatchStatus.Draft or MatchStatus.Open or MatchStatus.Closed))
        {
            throw new InvalidMatchStatusTransitionException(
                "Players can only be removed before the game starts.");
        }

        if (playerId == callerId)
        {
            throw new InvalidMatchStatusTransitionException("The organizer cannot remove themselves.");
        }

        var presence = await _presenceRepository.FindByMatchAndPlayerAsync(matchId, playerId, cancellationToken)
            ?? throw new PresenceNotFoundException(matchId, playerId);

        await _presenceRepository.DeleteAsync(presence, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        Guid? promoted = null;
        if (presence.Status == PresenceStatus.Confirmed)
        {
            var waiter = (await _waitingListRepository.ListByMatchAsync(matchId, cancellationToken))
                .FirstOrDefault(w => w.PlayerType == presence.PlayerType);
            if (waiter is not null)
            {
                await _presenceRepository.AddAsync(
                    MatchPresence.Create(matchId, waiter.PlayerId, waiter.PlayerType, now), cancellationToken);
                await _waitingListRepository.RemoveAsync(waiter, cancellationToken);
                promoted = waiter.PlayerId;
            }
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Presence removed. MatchId={MatchId} PlayerId={PlayerId} PlayerType={PlayerType} Status={Status} Promoted={Promoted}",
            matchId, playerId, presence.PlayerType, presence.Status, promoted is not null);

        if (promoted is { } promotedId)
        {
            await _eventPublisher.PublishAsync(new WaitingListPromoted(matchId, promotedId, now), cancellationToken);
        }

        var confirmedCount = await _presenceRepository.CountConfirmedAsync(matchId, presence.PlayerType, cancellationToken);
        await _roomNotifier.NotifyPresenceUpdatedAsync(
            new PresenceUpdatedMessage(
                MatchId: matchId,
                PlayerId: playerId,
                PlayerType: presence.PlayerType.ToString(),
                Status: "Removed",
                ConfirmedCount: confirmedCount,
                OccurredAt: now),
            cancellationToken);
    }
}
