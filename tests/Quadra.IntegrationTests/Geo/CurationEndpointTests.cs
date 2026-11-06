using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
/// The team's arena curation (DECISIONS #94) through the real endpoints: only admins reach it; the
/// candidates of an area come from a Google search the team asks for; each decision (approve,
/// link, reject) takes a place off the pending list for good, and undoing it brings it back.
/// </summary>
public sealed class CurationEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    private const double Lat = -23.66, Lon = -46.66;
    private static readonly Guid Admin = Guid.NewGuid();

    private readonly MatchesWebApplicationFactory _fx;

    public CurationEndpointTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Box => FormattableString.Invariant(
        $"south={Lat - 0.02}&west={Lon - 0.02}&north={Lat + 0.02}&east={Lon + 0.02}&lat={Lat}&lon={Lon}");

    [Fact]
    public async Task Only_the_team_reaches_curation()
    {
        var player = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var (admin, _) = AdminClient(new FixedVenueSource(new()));

        (await Read(await player.GetAsync("/api/v1/admin/access", Ct))).GetProperty("admin").GetBoolean().Should().BeFalse();
        (await Read(await admin.GetAsync("/api/v1/admin/access", Ct))).GetProperty("admin").GetBoolean().Should().BeTrue();

        (await player.GetAsync($"/api/v1/admin/arenas/candidates?{Box}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await player.PostAsJsonAsync("/api/v1/admin/arenas/decisions", new { placeId = "x", action = "Reject" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await player.DeleteAsync("/api/v1/admin/arenas/decisions/x", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await player.PostAsJsonAsync("/api/v1/admin/arenas/discover", new { lat = Lat, lon = Lon, radiusKm = 2 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _fx.CreateAnonymousClient().GetAsync("/api/v1/admin/access", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Each_decision_takes_a_place_off_the_list_and_undoing_it_brings_it_back()
    {
        await ResetAsync();
        var source = new FixedVenueSource(new()
        {
            ["c-main"] = (Lat, Lon),
            ["c-gate"] = (Lat + 0.0002, Lon),   // ~22 m: another entrance of the same place
            ["c-gym"] = (Lat + 0.009, Lon),     // ~1 km: an academia, not a court
        });
        var (admin, _) = AdminClient(source);
        await SeedGameAsync(Lat + 0.0001, Lon);

        (await Read(await admin.PostAsJsonAsync("/api/v1/admin/arenas/discover", new { lat = Lat, lon = Lon, radiusKm = 2 }, Ct)))
            .GetProperty("cellsSearched").GetInt32().Should().BeGreaterThan(0);
        (await Read(await admin.PostAsJsonAsync("/api/v1/admin/arenas/discover", new { lat = Lat, lon = Lon, radiusKm = 2 }, Ct)))
            .GetProperty("cellsSearched").GetInt32().Should().Be(0, "a fresh area is not searched again");

        var list = await CandidatesAsync(admin);
        list.GetProperty("pendingCount").GetInt32().Should().Be(3);
        var candidates = list.GetProperty("candidates").EnumerateArray().ToList();
        candidates[0].GetProperty("placeId").GetString().Should().Be("c-main", "places with games nearby come first");
        candidates[0].GetProperty("gamesNearby").GetInt32().Should().Be(1);
        candidates[0].GetProperty("neighbors").EnumerateArray().Select(n => n.GetString()).Should().Equal("c-gate");

        // Approve the main entrance: an active arena at the candidate's point, on the map.
        var approved = await Read(await admin.PostAsJsonAsync("/api/v1/admin/arenas/decisions",
            new { placeId = "c-main", action = "Approve", kind = "Sand", latitude = Lat, longitude = Lon }, Ct));
        var arenaId = approved.GetProperty("arenaId").GetGuid();
        (await LocationSourceAsync(arenaId)).Should().Be("Candidate");
        var map = await Read(await admin.GetAsync($"/api/v1/map/items?{Box}", Ct));
        map.GetProperty("arenas").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).Should().Contain(arenaId);

        // The gate is the same arena; the gym is no place to play.
        (await admin.PostAsJsonAsync("/api/v1/admin/arenas/decisions", new { placeId = "c-gate", action = "Link", arenaId }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.PostAsJsonAsync("/api/v1/admin/arenas/decisions", new { placeId = "c-gym", action = "Reject" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await CandidatesAsync(admin)).GetProperty("pendingCount").GetInt32().Should().Be(0);

        // A decided place cannot be decided again; unknown places and arenas are refused.
        (await admin.PostAsJsonAsync("/api/v1/admin/arenas/decisions", new { placeId = "c-gym", action = "Reject" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.PostAsJsonAsync("/api/v1/admin/arenas/decisions", new { placeId = "nope", action = "Reject" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await admin.PostAsJsonAsync("/api/v1/admin/arenas/decisions", new { placeId = "c-main", action = "Approve" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "approving needs a kind and a point");

        // Undo the gate, then the main entrance: the arena, left with no place, goes off the map.
        (await admin.DeleteAsync("/api/v1/admin/arenas/decisions/c-gate", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CandidatesAsync(admin)).GetProperty("pendingCount").GetInt32().Should().Be(1);
        (await admin.DeleteAsync("/api/v1/admin/arenas/decisions/c-main", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await CandidatesAsync(admin)).GetProperty("pendingCount").GetInt32().Should().Be(2);
        map = await Read(await admin.GetAsync($"/api/v1/map/items?{Box}", Ct));
        map.GetProperty("arenas").EnumerateArray().Select(a => a.GetProperty("id").GetGuid()).Should().NotContain(arenaId);
        (await admin.DeleteAsync("/api/v1/admin/arenas/decisions/c-main", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Approving again with the pin moved by the team records it as ours.
        var moved = await Read(await admin.PostAsJsonAsync("/api/v1/admin/arenas/decisions",
            new { placeId = "c-main", action = "Approve", kind = "Court", latitude = Lat + 0.0001, longitude = Lon, pinMoved = true }, Ct));
        (await LocationSourceAsync(moved.GetProperty("arenaId").GetGuid())).Should().Be("Curator");

        // Rejected stays rejected, even when the area is searched again later.
        (await CandidatesAsync(admin)).GetProperty("candidates").EnumerateArray()
            .Select(c => c.GetProperty("placeId").GetString()).Should().NotContain("c-gym");
    }

    private (HttpClient Client, IServiceProvider Services) AdminClient(IVenueSource source)
    {
        var factory = _fx.Factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Moderation:AdminUserIds:0"] = Admin.ToString(),
            }));
            builder.ConfigureTestServices(s =>
            {
                s.RemoveAll<IVenueSource>();
                s.AddSingleton(source);
            });
        });
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = _fx.CreateAuthenticatedClient(Admin).DefaultRequestHeaders.Authorization;
        return (client, factory.Services);
    }

    private async Task<JsonElement> CandidatesAsync(HttpClient admin) =>
        await Read(await admin.GetAsync($"/api/v1/admin/arenas/candidates?{Box}", Ct));

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task<string> LocationSourceAsync(Guid arenaId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<GeoDbContext>().Arenas
            .Where(a => a.Id == arenaId).Select(a => a.LocationSource).SingleAsync(Ct);
    }

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE waiting_list, match_presences, matches RESTART IDENTITY CASCADE;", Ct);
        await scope.ServiceProvider.GetRequiredService<GeoDbContext>().Database.ExecuteSqlRawAsync(
            "DELETE FROM arena_places; DELETE FROM arenas WHERE merged_into_id IS NOT NULL; DELETE FROM arenas; TRUNCATE TABLE venues, venue_areas;", Ct);
    }

    private async Task SeedGameAsync(double lat, double lon)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var now = DateTimeOffset.UtcNow;
        ctx.Matches.Add(Match.Create(
            Guid.NewGuid(), "Racha", null, "Rua Teste, 123", lat, lon, now.AddDays(2), 12, 12, null,
            MatchType.OneOff, null, null, windowOpensAt: now, windowClosesAt: now.AddDays(2), now: now));
        await ctx.SaveChangesAsync(Ct);
    }
}
