using FluentAssertions;
using NetTopologySuite.Geometries;
using Quadra.Modules.Geo.Application;
using Quadra.Modules.Geo.Venues;

namespace Quadra.UnitTests.Modules.Geo;

public sealed class VenueGridTests
{
    [Fact]
    public void Cells_cover_the_circle_and_start_with_the_one_holding_the_centre()
    {
        const double lat = -23.5505;
        const double lon = -46.6333;

        var cells = VenueGrid.CellsAround(lat, lon, radiusKm: 2);

        cells.Should().HaveCountLessThanOrEqualTo(4);
        var first = cells[0];
        lat.Should().BeInRange(first.South, first.North);
        lon.Should().BeInRange(first.West, first.East);
        cells.Should().OnlyHaveUniqueItems(c => c.Key);
    }

    [Fact]
    public void A_wider_view_needs_more_cells()
    {
        VenueGrid.CellsAround(-23.55, -46.63, radiusKm: 10).Count.Should().BeGreaterThan(9);
    }

    [Fact]
    public void Haversine_measures_meters()
    {
        var a = new Point(-46.6333, -23.5505);
        var b = new Point(-46.6333, -23.5505 + 0.0009); // 0.0009° of latitude ≈ 100 m

        Haversine.Meters(a, b).Should().BeApproximately(100, 1);
    }
}
