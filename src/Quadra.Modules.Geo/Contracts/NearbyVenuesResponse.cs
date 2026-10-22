namespace Quadra.Modules.Geo.Contracts;

/// <summary>
/// Response for <c>GET /api/v1/venues/nearby</c> (DECISIONS #82): the arenas in the area, closest
/// first, each with today's public games there. Name and address come from <c>GET /places/{placeId}</c>.
/// </summary>
public sealed record NearbyVenuesResponse(
    IReadOnlyList<NearbyVenueResponse> Items,
    int Count,
    bool HasMore = false);                     // more arenas in the radius than listed (closest kept)

public sealed record NearbyVenueResponse(
    string PlaceId,
    double Latitude,
    double Longitude,
    double DistanceKm,                         // from the query point, 3 decimals
    IReadOnlyList<VenueMatchResponse> Matches, // public games here today or happening now, earliest first
    string Kind = "Court");                    // Court | Sand | Park (DECISIONS #87)

public sealed record VenueMatchResponse(
    Guid Id,
    string Name,
    DateTimeOffset DateTime,
    bool Live);                                // the game is being played now
