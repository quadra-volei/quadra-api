using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quadra.IntegrationTests.Matches;
using Quadra.Modules.Geo.Arenas;
using Quadra.Modules.Geo.Persistence;
using Quadra.Modules.Geo.Venues;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.IntegrationTests.Geo;

/// <summary>
/// <c>GET /api/v1/map/items</c> and <c>GET /api/v1/arenas/{id}</c> (DECISIONS #93) against real
/// PostGIS: each game shows in one place only (under its arena, or loose), only public games in the
/// window count, and an arena that is not active never shows.
/// </summary>
public sealed class MapItemsEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    private const double Lat = -23.70, Lon = -46.70;

    private readonly MatchesWebApplicationFactory _fx;

    public MapItemsEndpointTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Box(string extra = "") => FormattableString.Invariant(
        $"/api/v1/map/items?south={Lat - 0.02}&west={Lon - 0.02}&north={Lat + 0.02}&east={Lon + 0.02}&lat={Lat}&lon={Lon}{extra}");

    [Fact]
    public async Task Every_game_shows_once_under_its_arena_or_loose_and_only_in_the_window()
    {
        await ResetAsync();
        var arena = await SaveArenaAsync(Lat, Lon, VenueKinds.Court, "p-map-a", ArenaStatuses.Active);
        var old = await SaveArenaAsync(Lat, Lon, VenueKinds.Court, null, ArenaStatuses.Merged, mergedInto: arena);
        var closed = await SaveArenaAsync(Lat + 0.005, Lon, VenueKinds.Sand, "p-map-closed", ArenaStatuses.Inactive);

        var live = await SeedAsync("Agora", Lat, Lon, arena, status: "InProgress", hoursAhead: -1);
        var soon = await SeedAsync("Quinta", Lat + 0.01, Lon, arena, hoursAhead: 72); // 1 km away, still the arena's
        var merged = await SeedAsync("Da fundida", Lat, Lon, old, hoursAhead: 24);
        await SeedAsync("Daqui a 10 dias", Lat, Lon, arena, hoursAhead: 240);
        await SeedAsync("Privada", Lat, Lon, arena, visibility: "Private", hoursAhead: 24);
        var nextDoor = await SeedAsync("Do lado, sem arena", Lat, Lon + 0.0002, null, hoursAhead: 24);
        var orphaned = await SeedAsync("Arena desativada", Lat + 0.005, Lon, closed, hoursAhead: 24);
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        var body = await GetAsync(client, Box());

        var arenas = body.GetProperty("arenas").EnumerateArray().ToList();
        arenas.Select(a => a.GetProperty("id").GetGuid()).Should().Equal(arena);
        var a = arenas[0];
        a.GetProperty("placeId").GetString().Should().Be("p-map-a");
        a.GetProperty("gamesCount").GetInt32().Should().Be(3, "live, in 3 days and the merged arena's; not the private one nor the one in 10 days");
        a.GetProperty("live").GetBoolean().Should().BeTrue();
        a.GetProperty("nextGames").EnumerateArray().Select(g => g.GetProperty("id").GetGuid())
            .Should().Equal(live, merged, soon);
        body.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo([nextDoor, orphaned], "a game with no active arena is a loose game, however close to one");
        body.GetProperty("arenaCount").GetInt32().Should().Be(1);
        body.GetProperty("gameCount").GetInt32().Should().Be(5);
        body.GetProperty("truncated").GetBoolean().Should().BeFalse();

        var tomorrow = await GetAsync(client, Box("&days=1"));
        tomorrow.GetProperty("arenas")[0].GetProperty("gamesCount").GetInt32().Should().Be(2, "the game in 3 days is outside one day");

        var sandOnly = await GetAsync(client, Box("&kinds=Sand"));
        sandOnly.GetProperty("arenas").GetArrayLength().Should().Be(0);
        sandOnly.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo([nextDoor, orphaned], "a filtered-out arena hides its games instead of turning them loose");
    }

    [Fact]
    public async Task The_arena_detail_lists_all_its_upcoming_public_games_and_hides_arenas_off_the_map()
    {
        await ResetAsync();
        var arena = await SaveArenaAsync(Lat, Lon, VenueKinds.Park, "p-detail", ArenaStatuses.Active);
        var old = await SaveArenaAsync(Lat, Lon, VenueKinds.Park, null, ArenaStatuses.Merged, mergedInto: arena);
        var draft = await SaveArenaAsync(Lat, Lon, VenueKinds.Park, "p-detail-draft", ArenaStatuses.Draft);
        var later = await SeedAsync("Daqui a 10 dias", Lat, Lon, arena, hoursAhead: 240);
        var soon = await SeedAsync("Amanhã", Lat, Lon, arena, hoursAhead: 24);
        await SeedAsync("Privada", Lat, Lon, arena, visibility: "Private", hoursAhead: 24);
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        var detail = await GetAsync(client, $"/api/v1/arenas/{old}");

        detail.GetProperty("id").GetGuid().Should().Be(arena, "a merged id answers with the arena that stayed");
        detail.GetProperty("placeId").GetString().Should().Be("p-detail");
        detail.GetProperty("kind").GetString().Should().Be("Park");
        detail.GetProperty("games").EnumerateArray().Select(g => g.GetProperty("id").GetGuid()).Should().Equal(soon, later);
        (await client.GetAsync($"/api/v1/arenas/{draft}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/v1/arenas/{Guid.NewGuid()}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_map_needs_a_login_and_a_sane_rectangle()
    {
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        (await _fx.CreateAnonymousClient().GetAsync(Box(), Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/map/items?south=-24&west=-47&north=-21&east=-46", Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "wider than 2 degrees");
        (await client.GetAsync("/api/v1/map/items?south=-23&west=-47&north=-24&east=-46", Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "south above north");
        (await client.GetAsync(Box("&days=31"), Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.GetAsync(Box("&kinds=Beach"), Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private async Task<JsonElement> GetAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE waiting_list, match_presences, matches RESTART IDENTITY CASCADE;", Ct);
        await scope.ServiceProvider.GetRequiredService<GeoDbContext>().Database.ExecuteSqlRawAsync(
            "DELETE FROM arena_places; DELETE FROM arenas WHERE merged_into_id IS NOT NULL; DELETE FROM arenas;", Ct);
    }

    private async Task<Guid> SaveArenaAsync(double lat, double lon, string kind, string? placeId, string status, Guid? mergedInto = null)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
        var now = DateTimeOffset.UtcNow;
        var reviewer = Guid.NewGuid();
        var arena = Arena.Create(lat, lon, kind, now);
        switch (status)
        {
            case ArenaStatuses.Active:
                arena.Approve(reviewer, now);
                break;
            case ArenaStatuses.Inactive:
                arena.Deactivate(reviewer, now);
                break;
            case ArenaStatuses.Merged:
                arena.MergeInto(await db.Arenas.SingleAsync(a => a.Id == mergedInto, Ct), reviewer, now);
                break;
        }

        db.Arenas.Add(arena);
        if (placeId is not null)
        {
            db.ArenaPlaces.Add(ArenaPlace.Link(placeId, arena.Id, isPrimary: true, reviewer, now));
        }

        await db.SaveChangesAsync(Ct);
        return arena.Id;
    }

    /// <summary>A public game <paramref name="hoursAhead"/> from now, linked to <paramref name="arenaId"/>; status and visibility set in SQL.</summary>
    private async Task<Guid> SeedAsync(
        string name, double lat, double lon, Guid? arenaId, string status = "Open", string visibility = "Open", int hoursAhead = 24)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var now = DateTimeOffset.UtcNow;
        var match = Match.Create(
            Guid.NewGuid(), name, null, "Rua Teste, 123", lat, lon, now.AddDays(7), 12, 12, null,
            MatchType.OneOff, null, null, windowOpensAt: now, windowClosesAt: now.AddDays(7), now: now);
        match.LinkArena(arenaId, now);
        ctx.Matches.Add(match);
        await ctx.SaveChangesAsync(Ct);
        await ctx.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE matches SET date_time = {now.AddHours(hoursAhead)}, status = {status}, visibility = {visibility} WHERE id = {match.Id}",
            Ct);
        return match.Id;
    }
}
