namespace Quadra.Modules.Geo.Application;

/// <summary>
/// Internal projection of a spatial candidate returned by the radius query, carrying the
/// straight-line distance (in meters) from the query point. Combined with slot availability
/// in <see cref="GetNearbyMatchesHandler"/> before being mapped to the API response.
/// </summary>
public sealed record NearbyMatchCandidate(
    Guid Id,
    string Name,
    string Address,
    double Latitude,
    double Longitude,
    DateTimeOffset DateTime,
    int MaxPlayers,
    decimal? Price,
    string Type,
    string Status,
    string? Format,
    string? Level,
    double DistanceMeters);
