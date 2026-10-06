using Microsoft.Extensions.Logging;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Lets the organizer fill a slot with a player who has no app account
/// (POST /api/v1/matches/{id}/guests). A guest counts as confirmed against the capacity.
/// </summary>
public sealed class AddMatchGuestHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly IMatchGuestRepository _guestRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<AddMatchGuestHandler> _logger;

    public AddMatchGuestHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IMatchGuestRepository guestRepository,
        TimeProvider timeProvider,
        ILogger<AddMatchGuestHandler> logger)
    {
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _guestRepository = guestRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MatchGuest> HandleAsync(
        Guid matchId,
        Guid callerId,
        AddGuestRequest request,
        CancellationToken cancellationToken)
    {
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        if (match.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        if (match.Status is not (MatchStatus.Draft or MatchStatus.Open or MatchStatus.Closed))
        {
            throw new InvalidMatchStatusTransitionException(
                $"Guests cannot be added to a match with status '{match.Status}'.");
        }

        var confirmed = await _presenceRepository.CountConfirmedAsync(matchId, cancellationToken);
        var guests = await _guestRepository.CountByMatchAsync(matchId, cancellationToken);
        if (confirmed + guests >= match.MaxPlayers)
        {
            throw new MatchFullException(matchId);
        }

        var guest = MatchGuest.Create(
            matchId,
            request.Name.Trim(),
            request.Position,
            callerId,
            _timeProvider.GetUtcNow());

        await _guestRepository.AddAsync(guest, cancellationToken);

        _logger.LogInformation("Guest added. MatchId={MatchId} GuestId={GuestId}", matchId, guest.Id);

        return guest;
    }
}

/// <summary>
/// Lets the organizer remove a guest, freeing the slot
/// (DELETE /api/v1/matches/{id}/guests/{guestId}).
/// </summary>
public sealed class RemoveMatchGuestHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IMatchGuestRepository _guestRepository;
    private readonly ILogger<RemoveMatchGuestHandler> _logger;

    public RemoveMatchGuestHandler(
        IMatchRepository matchRepository,
        IMatchGuestRepository guestRepository,
        ILogger<RemoveMatchGuestHandler> logger)
    {
        _matchRepository = matchRepository;
        _guestRepository = guestRepository;
        _logger = logger;
    }

    public async Task HandleAsync(
        Guid matchId,
        Guid callerId,
        Guid guestId,
        CancellationToken cancellationToken)
    {
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        if (match.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        if (match.Status is not (MatchStatus.Draft or MatchStatus.Open or MatchStatus.Closed))
        {
            throw new InvalidMatchStatusTransitionException(
                $"Guests cannot be removed from a match with status '{match.Status}'.");
        }

        var guest = await _guestRepository.FindAsync(matchId, guestId, cancellationToken)
            ?? throw new MatchGuestNotFoundException(matchId, guestId);

        await _guestRepository.RemoveAsync(guest, cancellationToken);

        _logger.LogInformation("Guest removed. MatchId={MatchId} GuestId={GuestId}", matchId, guestId);
    }
}
