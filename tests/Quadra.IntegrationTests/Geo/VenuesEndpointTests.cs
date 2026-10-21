using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.IntegrationTests.Matches;
using Quadra.Modules.Geo.Persistence;
using Quadra.Modules.Geo.Venues;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.IntegrationTests.Geo;

/// <summary>
/// <c>GET /api/v1/venues/nearby</c> (DECISIONS #82) against real PostGIS, with Google replaced by a
/// fixed in-memory source: the area is searched once and cached, and each arena lists only today's
/// public games played within 100 m of it.
/// </summary>
public sealed class VenuesEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    // São Paulo. Arena A sits on the query point, B ~1 km north. A 2 km radius covers at most
    // 4 grid cells, all searched in the first request.
    private const double Lat = -23.5505;
    private const double Lon = -46.6333;

    private readonly MatchesWebApplicationFactory _fx;

    public VenuesEndpointTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Lists_the_area_arenas_with_todays_public_games_and_searches_google_once()
    {
        await ResetAsync();
        var source = new FixedVenueSource(new()
        {
            ["arena-a"] = (Lat, Lon),
            ["arena-b"] = (Lat + 0.009, Lon),
        });
        var client = Client(source);

        var today = await SeedMatchAsync("Hoje", Lat + 0.0003, Lon); // ~33 m from arena A
        var live = await SeedMatchAsync("Agora", Lat, Lon + 0.0003, status: "InProgress");
        await SeedMatchAsync("Amanhã", Lat, Lon, dayOffset: 1);
        await SeedMatchAsync("Privada", Lat, Lon, visibility: "Private");
        await SeedMatchAsync("Rua", Lat + 0.003, Lon); // ~330 m: a loose court, no arena

        var first = await GetAsync(client);
        var searchesAfterFirst = source.Searches;
        var second = await GetAsync(client);

        var a = first.Single(v => v.GetProperty("placeId").GetString() == "arena-a");
        a.GetProperty("matches").EnumerateArray().Select(m => m.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo([today, live]);
        a.GetProperty("matches").EnumerateArray().Single(m => m.GetProperty("id").GetGuid() == live)
            .GetProperty("live").GetBoolean().Should().BeTrue();
        first.Single(v => v.GetProperty("placeId").GetString() == "arena-b")
            .GetProperty("matches").GetArrayLength().Should().Be(0);
        first[0].GetProperty("placeId").GetString().Should().Be("arena-a", "closest first");

        searchesAfterFirst.Should().BeGreaterThan(0);
        second.Should().HaveCount(2);
        source.Searches.Should().Be(searchesAfterFirst, "a fresh area is not searched again");
    }

    [Fact]
    public async Task Without_google_the_map_shows_no_arenas()
    {
        await ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        var items = await GetAsync(client);

        items.Should().BeEmpty();
    }

    [Fact]
    public async Task Requires_login()
    {
        var response = await _fx.Factory.CreateClient().GetAsync($"/api/v1/venues/nearby?lat={Lat}&lon={Lon}&radiusKm=5", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private HttpClient Client(IVenueSource source)
    {
        var factory = _fx.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IVenueSource>();
            services.AddSingleton(source);
        }));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            _fx.CreateAuthenticatedClient(Guid.NewGuid()).DefaultRequestHeaders.Authorization;
        return client;
    }

    private static async Task<JsonElement[]> GetAsync(HttpClient client)
    {
        var response = await client.GetAsync(
            $"/api/v1/venues/nearby?lat={Lat.ToString(System.Globalization.CultureInfo.InvariantCulture)}&lon={Lon.ToString(System.Globalization.CultureInfo.InvariantCulture)}&radiusKm=2",
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.GetProperty("items").EnumerateArray().ToArray();
    }

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE waiting_list, match_presences, matches RESTART IDENTITY CASCADE;", Ct);
        await scope.ServiceProvider.GetRequiredService<GeoDbContext>().Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE venues, venue_areas;", Ct);
    }

    /// <summary>An open match whose date is now (+ <paramref name="dayOffset"/> days), then adjusted in SQL.</summary>
    private async Task<Guid> SeedMatchAsync(
        string name, double lat, double lon, string status = "Open", string visibility = "Open", int dayOffset = 0)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var now = DateTimeOffset.UtcNow;
        var match = Match.Create(
            organizerId: Guid.NewGuid(),
            name: name,
            description: null,
            address: $"{name} Court",
            latitude: lat,
            longitude: lon,
            dateTime: now.AddDays(7),
            maxPlayers: 12,
            regularSlots: 12,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: now.AddDays(1),
            windowClosesAt: now.AddDays(5),
            now: now);
        ctx.Matches.Add(match);
        await ctx.SaveChangesAsync(Ct);

        // "Now" is always inside today in Brasília; now + 1 day never is.
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE matches SET date_time = {now.AddDays(dayOffset)}, status = {status}, visibility = {visibility} WHERE id = {match.Id}",
            Ct);
        return match.Id;
    }

    private sealed class FixedVenueSource : IVenueSource
    {
        private readonly Dictionary<string, (double Latitude, double Longitude)> _places;

        public FixedVenueSource(Dictionary<string, (double Latitude, double Longitude)> places)
        {
            _places = places;
        }

        private int _searches;

        public int Searches => _searches;

        public Task<IReadOnlyList<string>> SearchPlaceIdsAsync(
            string text, double south, double west, double north, double east, CancellationToken cancellationToken)
        {
            // The three queries of a cell run in parallel.
            Interlocked.Increment(ref _searches);
            IReadOnlyList<string> ids = _places
                .Where(p => p.Value.Latitude >= south && p.Value.Latitude < north
                    && p.Value.Longitude >= west && p.Value.Longitude < east)
                .Select(p => p.Key)
                .ToList();
            return Task.FromResult(ids);
        }

        public Task<(double Latitude, double Longitude)?> GetLocationAsync(string placeId, CancellationToken cancellationToken) =>
            Task.FromResult<(double Latitude, double Longitude)?>(_places.TryGetValue(placeId, out var p) ? p : null);
    }
}
