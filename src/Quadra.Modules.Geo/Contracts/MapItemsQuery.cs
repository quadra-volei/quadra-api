namespace Quadra.Modules.Geo.Contracts;

/// <summary>
/// Query for <c>GET /api/v1/map/items</c>: the visible map rectangle, the window of games and the
/// kinds of arena to show. <see cref="Lat"/>/<see cref="Lon"/> (the user) only sort the list by
/// distance; without them the rectangle's centre is used.
/// </summary>
public sealed record MapItemsQuery
{
    public double South { get; init; }
    public double West { get; init; }
    public double North { get; init; }
    public double East { get; init; }

    /// <summary>Games from now to this many days ahead (1–30), plus any being played. 7 when not sent.</summary>
    public int Days { get; init; } = 7;

    /// <summary>Comma-separated <c>Court,Sand,Park</c>; all when not sent. Filters arenas only.</summary>
    public string? Kinds { get; init; }

    public double? Lat { get; init; }
    public double? Lon { get; init; }
}
