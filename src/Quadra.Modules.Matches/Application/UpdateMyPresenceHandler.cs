using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;
using Quadra.Shared.Realtime;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Orchestrates updating the caller's own presence status (PUT /api/v1/matches/{id}/presences/me).
///
/// A caller who is not on the list yet joins by confirming: allowed for the organizer, for
/// anyone on an open match, and for holders of the invite code of a private match. They enter
/// as a Regular while the confirmation window is open, or as a DropIn once it has closed.
/// Handles waiting list insertion when all slots are full.
/// </summary>
public sealed class UpdateMyPresenceHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IMatchGuestRepository _guestRepository;
    private readonly MatchWindowSynchronizer _windowSynchronizer;
    private readonly IEventPublisher _eventPublisher;
    private readonly IMatchRoomNotifier _roomNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateMyPresenceHandler> _logger;

    public UpdateMyPresenceHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IWaitingListRepository waitingListRepository,
        IMatchGuestRepository guestRepository,
        MatchWindowSynchronizer windowSynchronizer,
        IEventPublisher eventPublisher,
        IMatchRoomNotifier roomNotifier,
        TimeProvider timeProvider,
        ILogger<UpdateMyPresenceHandler> logger)
    {
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _waitingListRepository = waitingListRepository;
        _guestRepository = guestRepository;
        _windowSynchronizer = windowSynchronizer;
        _eventPublisher = eventPublisher;
        _roomNotifier = roomNotifier;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MatchPresence> HandleAsync(
        Guid matchId,
        Guid callerId,
        UpdateMyPresenceRequest request,
        CancellationToken cancellationToken)
    {
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // The window only opens/closes when something looks at the match.
        match = await _windowSynchronizer.SyncAsync(match, cancellationToken);

        var desiredStatus = Enum.Parse<PresenceStatus>(request.Status, ignoreCase: true);
        var now = _timeProvider.GetUtcNow();

        var presence = await _presenceRepository.FindByMatchAndPlayerAsync(matchId, callerId, cancellationToken);
        var isJoining = presence is null;

        if (isJoining)
        {
            // Declining a match you are not part of means nothing.
            if (desiredStatus != PresenceStatus.Confirmed)
            {
                throw new PresenceNotFoundException(matchId, callerId);
            }

            if (!match.AllowsSelfEnrollment(callerId, request.InviteCode))
            {
                throw new MatchInviteRequiredException(matchId);
            }
        }

        // Someone joining after the window closed takes a drop-in slot.
        var playerType = presence?.PlayerType
            ?? (match.Status == MatchStatus.Closed ? PlayerType.DropIn : PlayerType.Regular);

        EnsureWindowAllows(match, playerType, now);

        if (desiredStatus == PresenceStatus.Declined)
        {
            presence!.Decline(now);
            await _presenceRepository.UpdateAsync(presence, cancellationToken);

            _logger.LogInformation(
                "Presence declined. MatchId={MatchId} PlayerId={PlayerId} PlayerType={PlayerType} Status={Status}",
                matchId, callerId, presence.PlayerType, presence.Status);

            // Promote first Regular waiter when a Regular declines.
            if (presence.PlayerType == PlayerType.Regular)
            {
                await PromoteFirstWaiterAsync(matchId, PlayerType.Regular, now, cancellationToken);
            }

            await PublishAndBroadcastAsync(match, presence, now, cancellationToken);
            return presence;
        }

        var confirmedCount = await _presenceRepository.CountConfirmedAsync(matchId, playerType, cancellationToken);
        var guestCount = await _guestRepository.CountByMatchAsync(matchId, cancellationToken);

        // Guests are added during the window, so they take up Regular slots.
        var slotLimit = playerType == PlayerType.Regular
            ? Math.Max(0, match.RegularSlots - guestCount)
            : match.DropInSlots + (match.ReleasedDropInSlots ?? 0);

        if (confirmedCount >= slotLimit)
        {
            // Check if player is already on the waiting list.
            var existingWaiter = await _waitingListRepository.FindByMatchAndPlayerAsync(
                matchId, callerId, cancellationToken);
            if (existingWaiter is not null)
            {
                throw new SlotLimitReachedException(); // already on waiting list
            }

            var position = await _waitingListRepository.GetNextPositionAsync(matchId, cancellationToken);
            var entry = WaitingListEntry.Create(matchId, callerId, playerType, position, now);
            await _waitingListRepository.AddAsync(entry, cancellationToken);

            throw new SlotLimitReachedException(position);
        }

        if (isJoining)
        {
            presence = MatchPresence.Create(matchId, callerId, playerType, now);
            presence.Confirm(now);
            try
            {
                await _presenceRepository.AddAsync(presence, cancellationToken);
            }
            catch (DbUpdateException)
            {
                // Unique constraint: a concurrent request of the same player joined first.
                throw new PresenceAlreadyExistsException(matchId, callerId);
            }
        }
        else
        {
            presence!.Confirm(now);
            await _presenceRepository.UpdateAsync(presence, cancellationToken);
        }

        _logger.LogInformation(
            "Presence confirmed. MatchId={MatchId} PlayerId={PlayerId} PlayerType={PlayerType} Status={Status} Joined={Joined}",
            matchId, callerId, presence.PlayerType, presence.Status, isJoining);

        var @event = new PresenceConfirmed(
            MatchId: matchId,
            PlayerId: callerId,
            PlayerType: presence.PlayerType.ToString(),
            ConfirmedAt: presence.ConfirmedAt!.Value,
            OccurredAt: now);

        await _eventPublisher.PublishAsync(@event, cancellationToken);
        await PublishAndBroadcastAsync(match, presence, now, cancellationToken);

        return presence;
    }

    private static void EnsureWindowAllows(Match match, PlayerType playerType, DateTimeOffset now)
    {
        if (playerType == PlayerType.Regular)
        {
            if (match.Status != MatchStatus.Open
                || now < match.WindowOpensAt
                || now > match.WindowClosesAt)
            {
                throw new PresenceWindowNotOpenException("Confirmation window is not open.");
            }
        }
        else // DropIn
        {
            if (match.Status != MatchStatus.Closed || now <= match.WindowClosesAt)
            {
                throw new PresenceWindowNotOpenException("DropIn slots are not yet available.");
            }
        }
    }

    private async Task PromoteFirstWaiterAsync(
        Guid matchId,
        PlayerType playerType,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var waiters = await _waitingListRepository.ListByMatchAsync(matchId, cancellationToken);
        var firstWaiter = waiters.FirstOrDefault(w => w.PlayerType == playerType);

        if (firstWaiter is null)
        {
            return;
        }

        var promotedPresence = MatchPresence.Create(matchId, firstWaiter.PlayerId, playerType, now);
        await _presenceRepository.AddAsync(promotedPresence, cancellationToken);
        await _waitingListRepository.RemoveAsync(firstWaiter, cancellationToken);

        _logger.LogInformation(
            "Waiting list player promoted. MatchId={MatchId} PlayerId={PlayerId} PlayerType={PlayerType} Status={Status}",
            matchId, firstWaiter.PlayerId, playerType, promotedPresence.Status);
    }

    private async Task PublishAndBroadcastAsync(
        Entities.Match match,
        MatchPresence presence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var confirmedCount = await _presenceRepository.CountConfirmedAsync(
            match.Id, presence.PlayerType, cancellationToken);

        var message = new PresenceUpdatedMessage(
            MatchId: match.Id,
            PlayerId: presence.PlayerId,
            PlayerType: presence.PlayerType.ToString(),
            Status: presence.Status.ToString(),
            ConfirmedCount: confirmedCount,
            OccurredAt: now);

        await _roomNotifier.NotifyPresenceUpdatedAsync(message, cancellationToken);
    }
}
