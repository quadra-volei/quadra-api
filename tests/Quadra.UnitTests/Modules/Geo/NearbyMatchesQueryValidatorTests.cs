using FluentAssertions;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Validation;

namespace Quadra.UnitTests.Modules.Geo;

/// <summary>
/// Unit tests for <see cref="NearbyMatchesQueryValidator"/> — the FluentValidation rules guarding the
/// <c>GET /api/v1/matches/nearby</c> query parameters (Lat range, Lon range, RadiusKm bounds).
/// Supports AC-1/AC-2/AC-3 by ensuring only well-formed spatial queries reach the handler.
/// </summary>
public sealed class NearbyMatchesQueryValidatorTests
{
    private readonly NearbyMatchesQueryValidator _sut = new();

    [Fact]
    public void Valid_query_passes_validation()
    {
        var result = _sut.Validate(new NearbyMatchesQuery(Lat: -23.5, Lon: -46.6, RadiusKm: 10));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-90.0)]
    [InlineData(0.0)]
    [InlineData(90.0)]
    public void Lat_within_range_is_valid(double lat)
    {
        var result = _sut.Validate(new NearbyMatchesQuery(Lat: lat, Lon: 0, RadiusKm: 5));
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(-90.1)]
    [InlineData(90.1)]
    public void Lat_out_of_range_is_invalid(double lat)
    {
        var result = _sut.Validate(new NearbyMatchesQuery(Lat: lat, Lon: 0, RadiusKm: 5));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(NearbyMatchesQuery.Lat));
    }

    [Theory]
    [InlineData(-180.1)]
    [InlineData(180.1)]
    public void Lon_out_of_range_is_invalid(double lon)
    {
        var result = _sut.Validate(new NearbyMatchesQuery(Lat: 0, Lon: lon, RadiusKm: 5));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(NearbyMatchesQuery.Lon));
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(50.1)]
    [InlineData(100.0)]
    public void RadiusKm_out_of_bounds_is_invalid(double radiusKm)
    {
        var result = _sut.Validate(new NearbyMatchesQuery(Lat: 0, Lon: 0, RadiusKm: radiusKm));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(NearbyMatchesQuery.RadiusKm));
    }

    [Theory]
    [InlineData(0.001)]
    [InlineData(25.0)]
    [InlineData(50.0)]
    public void RadiusKm_within_bounds_is_valid(double radiusKm)
    {
        var result = _sut.Validate(new NearbyMatchesQuery(Lat: 0, Lon: 0, RadiusKm: radiusKm));
        result.IsValid.Should().BeTrue();
    }
}
