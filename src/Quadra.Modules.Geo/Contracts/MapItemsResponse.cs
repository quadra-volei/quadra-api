namespace Quadra.Modules.Geo.Contracts;

/// <summary>
/// Response for <c>GET /api/v1/map/items</c> (DECISIONS #93): what the map and its list show for a
/// rectangle. A game is in exactly one place: under its arena when it has one, otherwise in
/// <see cref="Games"/>. Only public games count. The counts cover the whole rectangle, also what
/// was cut by the limits (<see cref="Truncated"/>).
/// </summary>
public sealed record MapItemsResponse(
    IReadOnlyList<MapArenaResponse> Arenas,   // closest first
    IReadOnlyList<NearbyMatchResponse> Games, // games with no arena, closest first
    int ArenaCount,
    int GameCount,                            // games at the arenas plus the loose ones
    bool Truncated);

/// <summary>An approved arena; its name comes from <c>GET /places/{placeId}</c>.</summary>
public sealed record MapArenaResponse(
    Guid Id,
    string? PlaceId,
    double Latitude,
    double Longitude,
    double DistanceKm,
    string Kind,                                    // Court | Sand | Park
    int GamesCount,                                 // its games in the window
    bool Live,                                      // one of them is being played now
    IReadOnlyList<MapArenaGameResponse> NextGames); // being played first, then the next ones, at most 3

public sealed record MapArenaGameResponse(
    Guid Id,
    string Name,
    DateTimeOffset DateTime,
    bool Live,
    bool Full,                                      // no open slot: waiting list only
    int ConfirmedCount,
    int MaxPlayers);

/// <summary>Response for <c>GET /api/v1/arenas/{id}</c>: the arena and all its upcoming public games.</summary>
public sealed record ArenaDetailResponse(
    Guid Id,
    string? PlaceId,
    double Latitude,
    double Longitude,
    string Kind,
    IReadOnlyList<MapArenaGameResponse> Games);     // being played first, then by date, at most 50
