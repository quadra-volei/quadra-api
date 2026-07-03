namespace Quadra.Modules.Geo.Contracts;

/// <summary>
/// Query parameters for <c>GET /api/v1/matches/nearby</c>.
/// </summary>
public sealed record NearbyMatchesQuery(
    double Lat,
    double Lon,
    double RadiusKm,
    bool OpenToDropIns = false);
