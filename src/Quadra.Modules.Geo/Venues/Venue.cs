using NetTopologySuite.Geometries;

namespace Quadra.Modules.Geo.Venues;

/// <summary>
/// An arena on the map, found through Google Places (DECISIONS #82). Google's terms let us keep
/// the place id but only cache coordinates for 30 days, so <see cref="FetchedAt"/> drives a
/// refresh before that and a purge after it. Name and address are never stored: the app asks
/// <c>GET /places/{id}</c> when the arena is tapped.
/// </summary>
public sealed class Venue
{
    private Venue() { }

    public Guid Id { get; private set; }

    /// <summary>Google Places id.</summary>
    public string PlaceId { get; private set; } = null!;

    /// <summary>PostGIS geography point (SRID 4326). X = Longitude, Y = Latitude.</summary>
    public Point Location { get; private set; } = null!;

    /// <summary>When the coordinates came from Google; they are deleted 30 days later.</summary>
    public DateTimeOffset FetchedAt { get; private set; }

    /// <summary>
    /// What kind of place it is (<see cref="VenueKinds"/>), from which of our own searches found
    /// it — never Google's place types, which may not be stored (DECISIONS #87).
    /// </summary>
    public string Kind { get; private set; } = VenueKinds.Court;
}

/// <summary>The kinds of arena the map filters by.</summary>
public static class VenueKinds
{
    /// <summary>Quadra convencional (indoor or hard court).</summary>
    public const string Court = "Court";

    /// <summary>Quadra de areia.</summary>
    public const string Sand = "Sand";

    public const string Park = "Park";

    /// <summary>A place found by several searches is the most specific kind: park, then sand.</summary>
    public static int Rank(string kind) => kind switch
    {
        Park => 2,
        Sand => 1,
        _ => 0,
    };
}

/// <summary>
/// A grid cell (<see cref="VenueGrid"/>) whose arenas were looked up at <see cref="DiscoveredAt"/>.
/// Holds nothing from Google, only when to look again.
/// </summary>
public sealed class VenueArea
{
    private VenueArea() { }

    public string Key { get; private set; } = null!;

    public DateTimeOffset DiscoveredAt { get; private set; }
}
