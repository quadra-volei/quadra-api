using NetTopologySuite.Geometries;

namespace Quadra.Modules.Geo.Persistence;

/// <summary>
/// Read projection over the Matches-owned <c>matches</c> table. Carries only the columns Geo is
/// permitted to read under the ARCHITECTURE-sanctioned SELECT grant. Not an entity Geo owns —
/// Geo emits no migrations for this table.
/// </summary>
public sealed class NearbyMatchRecord
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;

    /// <summary>
    /// PostGIS geography point (SRID 4326). X = Longitude, Y = Latitude.
    /// </summary>
    public Point Location { get; private set; } = null!;

    public DateTimeOffset DateTime { get; private set; }
    public short MaxPlayers { get; private set; }
    public decimal? Price { get; private set; }
    public string Type { get; private set; } = null!;
    public string Status { get; private set; } = null!;
}
