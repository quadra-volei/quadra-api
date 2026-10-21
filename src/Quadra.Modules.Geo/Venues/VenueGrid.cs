namespace Quadra.Modules.Geo.Venues;

/// <summary>
/// The fixed grid arena discovery works on: each cell is searched on Google once and looked up
/// again only when its result gets old (<see cref="VenueDiscovery"/>).
/// </summary>
public static class VenueGrid
{
    /// <summary>Cell side, about 5.5 km of latitude.</summary>
    public const double CellDegrees = 0.05;

    private const double KmPerDegree = 111.0;

    /// <summary>The cells overlapping the square around the circle, the one holding the centre first.</summary>
    public static IReadOnlyList<VenueCell> CellsAround(double latitude, double longitude, double radiusKm)
    {
        var dLat = radiusKm / KmPerDegree;
        var dLon = radiusKm / (KmPerDegree * Math.Cos(latitude * Math.PI / 180));

        var cells = new List<VenueCell>();
        for (var row = Index(latitude - dLat); row <= Index(latitude + dLat); row++)
        {
            for (var col = Index(longitude - dLon); col <= Index(longitude + dLon); col++)
            {
                cells.Add(new VenueCell(row, col));
            }
        }

        return cells
            .OrderBy(c => Math.Abs(c.CenterLatitude - latitude) + Math.Abs(c.CenterLongitude - longitude))
            .ToList();
    }

    private static int Index(double degrees) => (int)Math.Floor(degrees / CellDegrees);
}

/// <summary>One grid cell: rows go north, columns go east.</summary>
public sealed record VenueCell(int Row, int Col)
{
    public string Key => $"{Row}:{Col}";

    public double South => Row * VenueGrid.CellDegrees;

    public double West => Col * VenueGrid.CellDegrees;

    public double North => South + VenueGrid.CellDegrees;

    public double East => West + VenueGrid.CellDegrees;

    public double CenterLatitude => South + (VenueGrid.CellDegrees / 2);

    public double CenterLongitude => West + (VenueGrid.CellDegrees / 2);
}
