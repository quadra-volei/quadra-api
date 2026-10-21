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
