namespace Quadra.Modules.Geo.Contracts;

/// <summary>
/// Response for <c>GET /api/v1/matches/nearby</c>. <see cref="Items"/> are ordered by
/// <see cref="NearbyMatchResponse.DistanceKm"/> ascending.
/// </summary>
public sealed record NearbyMatchesResponse(
    IReadOnlyList<NearbyMatchResponse> Items,
    int Count);

/// <summary>
/// A single nearby match with open slots.
/// </summary>
public sealed record NearbyMatchResponse(
    Guid Id,
    string Name,
    string Address,
    double Latitude,
    double Longitude,
    double DistanceKm,               // straight-line distance from the query point, 3 decimals
    DateTimeOffset DateTime,
    int MaxPlayers,
    int OpenSlots,                   // Math.Max(0, MaxPlayers - ConfirmedCount)
    bool OpenToDropIns,              // a DropIn can still join per Matches slot rules
    decimal? Price,
    string Type,                     // Recurring | OneOff
    string Status,                   // Draft | Open | Closed
    int ConfirmedCount = 0,          // confirmed players plus guests
    string? Format = null,           // 2X2 | 4X4 | 6X6
    string? Level = null);           // Beginner | Intermediate | Advanced
