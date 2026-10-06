namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// 200 OK body for <c>GET /api/v1/matches/mine</c>: the caller's upcoming matches (organized,
/// on the presence list and not declined, or on the waiting list), soonest first.
/// </summary>
public sealed record MyMatchesResponse(IReadOnlyList<MyMatchResponse> Items);

/// <param name="ConfirmedCount">Confirmed players plus guests.</param>
/// <param name="OpenSlots"><c>MaxPlayers - ConfirmedCount</c>, never negative.</param>
/// <param name="ConfirmedPhotoUrls">Photos of up to four confirmed players who have one.</param>
/// <param name="IsOrganizer">Whether the caller organizes this match.</param>
/// <param name="MyStatus">The caller's presence status (Pending | Confirmed | Declined), or null.</param>
public sealed record MyMatchResponse(
    MatchResponse Match,
    int ConfirmedCount,
    int OpenSlots,
    IReadOnlyList<string> ConfirmedPhotoUrls,
    bool IsOrganizer,
    string? MyStatus);
