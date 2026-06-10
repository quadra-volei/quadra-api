using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Realtime;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Orchestrates adding a player to a match's presence list (POST /api/v1/matches/{id}/presences).
/// Only the match organizer may add players.
/// </summary>
public sealed class AddPresenceHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly IMatchRoomNotifier _roomNotifier;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AddPresenceHandler> _logger;

    public AddPresenceHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IMatchRoomNotifier roomNotifier,
        TimeProvider timeProvider,
        ILogger<AddPresenceHandler> logger)
    {
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _roomNotifier = roomNotifier;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MatchPresence> HandleAsync(
        Guid matchId,
        Guid callerId,
        AddPresenceRequest request,
        CancellationToken cancellationToken)
    {
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        if (match.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        if (match.Status is not (MatchStatus.Draft or MatchStatus.Open))
        {
            throw new PresenceWindowNotOpenException("Match is not in a state that accepts new presences.");
        }

        var now = _timeProvider.GetUtcNow();

        if (now > match.WindowClosesAt)
        {
            throw new PresenceWindowNotOpenException("Confirmation window has already closed.");
        }

        var existing = await _presenceRepository.FindByMatchAndPlayerAsync(
            matchId, request.PlayerId, cancellationToken);

        if (existing is not null)
        {
            throw new PresenceAlreadyExistsException(matchId, request.PlayerId);
        }

        var playerType = Enum.Parse<PlayerType>(request.PlayerType, ignoreCase: true);
        var presence = MatchPresence.Create(matchId, request.PlayerId, playerType, now);

        try
        {
            await _presenceRepository.AddAsync(presence, cancellationToken);
        }
        catch (DbUpdateException)
        {
            // Unique constraint violation — concurrent request already inserted a row.
            throw new PresenceAlreadyExistsException(matchId, request.PlayerId);
        }

        _logger.LogInformation(
            "Presence added. MatchId={MatchId} PlayerId={PlayerId} PlayerType={PlayerType} Status={Status}",
            matchId, request.PlayerId, playerType, presence.Status);

        var confirmedCount = await _presenceRepository.CountConfirmedAsync(matchId, playerType, cancellationToken);

        var message = new PresenceUpdatedMessage(
            MatchId: matchId,
            PlayerId: request.PlayerId,
            PlayerType: playerType.ToString(),
            Status: presence.Status.ToString(),
            ConfirmedCount: confirmedCount,
            OccurredAt: now);

        await _roomNotifier.NotifyPresenceUpdatedAsync(message, cancellationToken);

        return presence;
    }
}
