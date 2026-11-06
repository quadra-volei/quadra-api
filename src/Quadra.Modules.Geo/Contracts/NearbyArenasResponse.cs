namespace Quadra.Modules.Geo.Contracts;

/// <summary>
/// Response for <c>GET /api/v1/arenas/near</c>: the approved arenas a game picked at a spot may be
/// at. The app links a <see cref="NearbyArenaResponse.Linked"/> one as is and asks "É no …?" for
/// the others. Names come from <c>GET /places/{placeId}</c>; nothing from Google is stored.
/// </summary>
public sealed record NearbyArenasResponse(IReadOnlyList<NearbyArenaResponse> Items);

public sealed record NearbyArenaResponse(
    Guid Id,
    string? PlaceId,       // the arena's main Google entry, for its name
    int DistanceMeters,
    string Kind,           // Court | Sand | Park
    bool Linked);          // the picked place is this arena's own entry
