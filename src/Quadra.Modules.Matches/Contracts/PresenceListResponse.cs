namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Read model returned by <c>GET /api/v1/matches/{id}/presences</c>.
/// </summary>
public sealed record PresenceListResponse(
    IReadOnlyList<PresenceResponse> Confirmed,
    IReadOnlyList<PresenceResponse> Pending,
    IReadOnlyList<PresenceResponse> Declined,
    IReadOnlyList<WaitingListEntryResponse> WaitingList,
    int ConfirmedCount,
    int RegularSlots,
    int DropInSlots);
