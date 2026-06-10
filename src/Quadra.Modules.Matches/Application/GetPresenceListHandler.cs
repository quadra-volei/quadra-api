using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Loads all presence and waiting list records for a match (GET /api/v1/matches/{id}/presences).
/// </summary>
public sealed class GetPresenceListHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly IWaitingListRepository _waitingListRepository;

    public GetPresenceListHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IWaitingListRepository waitingListRepository)
    {
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _waitingListRepository = waitingListRepository;
    }

    public async Task<PresenceListResponse> HandleAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        var presences = await _presenceRepository.ListByMatchAsync(matchId, cancellationToken);
        var waitingList = await _waitingListRepository.ListByMatchAsync(matchId, cancellationToken);

        var confirmed = presences
            .Where(p => p.Status == PresenceStatus.Confirmed)
            .Select(ToPresenceResponse)
            .ToList();

        var pending = presences
            .Where(p => p.Status == PresenceStatus.Pending)
            .Select(ToPresenceResponse)
            .ToList();

        var declined = presences
            .Where(p => p.Status == PresenceStatus.Declined)
            .Select(ToPresenceResponse)
            .ToList();

        var waitingListResponse = waitingList
            .Select(w => new WaitingListEntryResponse(
                PlayerId: w.PlayerId,
                PlayerType: w.PlayerType.ToString(),
                Position: w.Position,
                CreatedAt: w.CreatedAt))
            .ToList();

        return new PresenceListResponse(
            Confirmed: confirmed,
            Pending: pending,
            Declined: declined,
            WaitingList: waitingListResponse,
            ConfirmedCount: confirmed.Count,
            RegularSlots: match.RegularSlots,
            DropInSlots: match.DropInSlots);
    }

    private static PresenceResponse ToPresenceResponse(MatchPresence p) =>
        new(
            Id: p.Id,
            MatchId: p.MatchId,
            PlayerId: p.PlayerId,
            PlayerType: p.PlayerType.ToString(),
            Status: p.Status.ToString(),
            ConfirmedAt: p.ConfirmedAt,
            CreatedAt: p.CreatedAt,
            UpdatedAt: p.UpdatedAt);
}
