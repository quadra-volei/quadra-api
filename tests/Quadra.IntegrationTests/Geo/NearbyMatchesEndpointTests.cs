using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quadra.IntegrationTests.Matches;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.IntegrationTests.Geo;

/// <summary>
/// HTTP integration tests for GET /api/v1/matches/nearby (F1.7 — Matches Map).
///
/// Exercises the real PostGIS <c>ST_DWithin</c> radius query over the <c>matches.location</c>
/// <c>geography(Point, 4326)</c> column against a real Postgres (postgis/postgis:16-3.4) via
/// Testcontainers, combined with the cross-module slot-availability lookup.
///
/// Reuses <see cref="MatchesWebApplicationFactory"/>, which boots the full app (both the Matches
/// and Geo modules are registered in Program.cs) and applies the Matches migration that creates the
/// <c>matches</c> table and its GIST spatial index.
///
/// Covers acceptance criteria:
///   - AC-1: returns matches within radius with OpenSlots &gt; 0, ordered by DistanceKm ascending;
///           matches with no open slots are excluded.
///   - AC-2: openToDropIns=true returns only matches with an open DropIn slot; openToDropIns=false
///           returns all matches with open slots.
///   - AC-3: PostGIS ST_DWithin radius filter — a match just outside the radius is excluded, one
///           inside is included.
/// </summary>
public sealed class NearbyMatchesEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    private readonly MatchesWebApplicationFactory _fx;
    private static readonly Guid CallerId = Guid.NewGuid();

    // Reference query point (São Paulo). PostGIS Point: X = longitude, Y = latitude.
    private const double QueryLat = -23.5505;
    private const double QueryLon = -46.6333;

    public NearbyMatchesEndpointTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    // ─── AC-3: PostGIS ST_DWithin radius filter ──────────────────────────────

    /// <summary>
    /// Covers: AC-3 — the radius filter is a real PostGIS ST_DWithin over geography(Point,4326):
    /// a match ~9 km from the query point is included within a 10 km radius, and a match ~11 km away
    /// is excluded. (~0.081 deg lat ≈ 9 km; ~0.099 deg lat ≈ 11 km at this latitude.)
    /// </summary>
    [Fact]
    public async Task GET_nearby_includes_match_inside_radius_and_excludes_match_outside_radius()
    {
        await ResetAsync();

        // Inside 10 km.
        var inside = await SeedMatchAsync(
            name: "Inside Radius",
            lat: QueryLat + 0.081,
            lon: QueryLon,
            maxPlayers: 10,
            confirmedRegular: 0);

        // Outside 10 km.
        await SeedMatchAsync(
            name: "Outside Radius",
            lat: QueryLat + 0.099,
            lon: QueryLon,
            maxPlayers: 10,
            confirmedRegular: 0);

        var items = await GetNearbyAsync(radiusKm: 10);

        items.Should().ContainSingle();
        items[0].GetProperty("id").GetGuid().Should().Be(inside);
        items[0].GetProperty("name").GetString().Should().Be("Inside Radius");
    }

    // ─── AC-1: open-slot filter + distance ordering ──────────────────────────

    /// <summary>
    /// Covers: AC-1 — within the radius, matches with OpenSlots &gt; 0 are returned ordered by
    /// DistanceKm ascending, and a fully-booked match (OpenSlots == 0) is excluded from Items.
    /// </summary>
    [Fact]
    public async Task GET_nearby_returns_open_matches_ordered_by_distance_and_excludes_full_matches()
    {
        await ResetAsync();

        // near (~2.2 km) — has open slots.
        var near = await SeedMatchAsync(
            name: "Near Open", lat: QueryLat + 0.02, lon: QueryLon, maxPlayers: 10, confirmedRegular: 2);

        // mid (~4.4 km) — full, no open slots → excluded.
        await SeedMatchAsync(
            name: "Mid Full", lat: QueryLat + 0.04, lon: QueryLon, maxPlayers: 4, confirmedRegular: 4);

        // far (~6.7 km) — has open slots.
        var far = await SeedMatchAsync(
            name: "Far Open", lat: QueryLat + 0.06, lon: QueryLon, maxPlayers: 10, confirmedRegular: 1);

        var items = await GetNearbyAsync(radiusKm: 10);

        items.Should().HaveCount(2);
        items.Select(i => i.GetProperty("id").GetGuid())
            .Should().ContainInOrder(near, far);

        // Distances strictly ascending.
        var distances = items.Select(i => i.GetProperty("distanceKm").GetDouble()).ToArray();
        distances.Should().BeInAscendingOrder();

        // OpenSlots reported per the availability reader.
        items[0].GetProperty("openSlots").GetInt32().Should().Be(8); // 10 - 2
        items[1].GetProperty("openSlots").GetInt32().Should().Be(9); // 10 - 1
    }

    // ─── AC-2: filter by matches open to DropIns ─────────────────────────────

    /// <summary>
    /// Covers: AC-2 — openToDropIns=true returns only matches where a DropIn can still join.
    /// The "no open DropIn" match still has open Regular slots (OpenSlots &gt; 0), proving the filter
    /// keys off DropIn availability, not overall open slots.
    /// </summary>
    [Fact]
    public async Task GET_nearby_openToDropIns_true_returns_only_matches_with_open_dropin_slot()
    {
        await ResetAsync();

        // maxPlayers 4, regularSlots 2, dropInSlots 2, no confirmations → open DropIn slot.
        var openDropIn = await SeedMatchAsync(
            name: "Open DropIn", lat: QueryLat + 0.02, lon: QueryLon,
            maxPlayers: 4, regularSlots: 2, confirmedRegular: 0, confirmedDropIn: 0);

        // dropInSlots 2 both taken (status Open → no released slots) but 2 Regular slots still open.
        await SeedMatchAsync(
            name: "No Open DropIn", lat: QueryLat + 0.03, lon: QueryLon,
            maxPlayers: 4, regularSlots: 2, confirmedRegular: 0, confirmedDropIn: 2);

        var items = await GetNearbyAsync(radiusKm: 10, openToDropIns: true);

        items.Should().ContainSingle();
        items[0].GetProperty("id").GetGuid().Should().Be(openDropIn);
        items[0].GetProperty("openToDropIns").GetBoolean().Should().BeTrue();
    }

    /// <summary>
    /// Covers: AC-2 — openToDropIns=false (default) returns all matches with open slots regardless of
    /// DropIn availability.
    /// </summary>
    [Fact]
    public async Task GET_nearby_openToDropIns_false_returns_all_matches_with_open_slots()
    {
        await ResetAsync();

        var openDropIn = await SeedMatchAsync(
            name: "Open DropIn", lat: QueryLat + 0.02, lon: QueryLon,
            maxPlayers: 4, regularSlots: 2, confirmedRegular: 0, confirmedDropIn: 0);

        var noOpenDropIn = await SeedMatchAsync(
            name: "No Open DropIn", lat: QueryLat + 0.03, lon: QueryLon,
            maxPlayers: 4, regularSlots: 2, confirmedRegular: 0, confirmedDropIn: 2);

        var items = await GetNearbyAsync(radiusKm: 10, openToDropIns: false);

        items.Select(i => i.GetProperty("id").GetGuid())
            .Should().BeEquivalentTo(new[] { openDropIn, noOpenDropIn });
    }

    // ─── endpoint guards ─────────────────────────────────────────────────────

    /// <summary>
    /// Covers: endpoint requires JWT — unauthenticated caller gets 401.
    /// </summary>
    [Fact]
    public async Task GET_nearby_without_auth_returns_401()
    {
        var client = _fx.CreateAnonymousClient();
        var response = await client.GetAsync(
            $"/api/v1/matches/nearby?lat={QueryLat}&lon={QueryLon}&radiusKm=10",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers: invalid query params (RadiusKm above the 50 km cap) return 400.
    /// </summary>
    [Fact]
    public async Task GET_nearby_with_radius_above_cap_returns_400()
    {
        var client = _fx.CreateAuthenticatedClient(CallerId);
        var response = await client.GetAsync(
            $"/api/v1/matches/nearby?lat={QueryLat}&lon={QueryLon}&radiusKm=51",
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Covers: AC-3 — an upcoming match well outside the radius simply does not appear; when there are
    /// no spatial candidates the endpoint returns 200 with an empty Items array.
    /// </summary>
    [Fact]
    public async Task GET_nearby_returns_empty_when_no_matches_in_radius()
    {
        await ResetAsync();

        await SeedMatchAsync(
            name: "Far Away", lat: QueryLat + 0.2, lon: QueryLon, maxPlayers: 10, confirmedRegular: 0);

        var client = _fx.CreateAuthenticatedClient(CallerId);
        var response = await client.GetAsync(
            $"/api/v1/matches/nearby?lat={QueryLat}&lon={QueryLon}&radiusKm=5",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("count").GetInt32().Should().Be(0);
        body.GetProperty("items").GetArrayLength().Should().Be(0);
    }

    /// <summary>
    /// Covers: private matches are reached by invitation and never listed; a match whose
    /// confirmation window has not opened yet (Draft) is listed, with its format and level.
    /// </summary>
    [Fact]
    public async Task GET_nearby_hides_private_matches_and_lists_matches_not_open_yet()
    {
        await ResetAsync();

        await SeedMatchAsync(
            name: "Private", lat: QueryLat, lon: QueryLon, maxPlayers: 10, confirmedRegular: 0,
            settings: new MatchSettings(Visibility: MatchVisibility.Private, InviteMode: MatchInviteMode.Code));
        await SeedMatchAsync(
            name: "Not Open Yet", lat: QueryLat, lon: QueryLon, maxPlayers: 10, confirmedRegular: 0,
            settings: new MatchSettings(Format: "4X4", Level: MatchLevel.Advanced), open: false);

        var items = await GetNearbyAsync(radiusKm: 5);

        items.Should().ContainSingle();
        items[0].GetProperty("name").GetString().Should().Be("Not Open Yet");
        items[0].GetProperty("status").GetString().Should().Be("Draft");
        items[0].GetProperty("format").GetString().Should().Be("4X4");
        items[0].GetProperty("level").GetString().Should().Be("Advanced");
        items[0].GetProperty("confirmedCount").GetInt32().Should().Be(0);
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private async Task<IReadOnlyList<JsonElement>> GetNearbyAsync(double radiusKm, bool openToDropIns = false)
    {
        var client = _fx.CreateAuthenticatedClient(CallerId);
        var url =
            $"/api/v1/matches/nearby?lat={QueryLat}&lon={QueryLon}&radiusKm={radiusKm}&openToDropIns={openToDropIns.ToString().ToLowerInvariant()}";

        var response = await client.GetAsync(url, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var items = body.GetProperty("items").EnumerateArray().ToArray();
        body.GetProperty("count").GetInt32().Should().Be(items.Length);
        return items;
    }

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE waiting_list, match_presences, matches RESTART IDENTITY CASCADE;",
            TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Seeds a match (via the Matches DbContext) at the given coordinates, transitions it to Open so it
    /// is joinable, and confirms the requested number of Regular/DropIn presences to control occupancy.
    /// </summary>
    private async Task<Guid> SeedMatchAsync(
        string name,
        double lat,
        double lon,
        int maxPlayers,
        int confirmedRegular,
        int? regularSlots = null,
        int confirmedDropIn = 0,
        MatchSettings? settings = null,
        bool open = true)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();

        var now = DateTimeOffset.UtcNow;
        var regular = regularSlots ?? maxPlayers; // by default all slots are Regular

        var match = Match.Create(
            organizerId: Guid.NewGuid(),
            name: name,
            description: null,
            address: $"{name} Court",
            latitude: lat,
            longitude: lon,
            dateTime: now.AddDays(7),      // upcoming
            maxPlayers: maxPlayers,
            regularSlots: regular,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: now.AddDays(1),
            windowClosesAt: now.AddDays(5),
            now: now,
            settings: settings);

        // Open by default; a match left in Draft has its window still to come.
        if (open)
        {
            match.OpenWindow(now);
        }

        ctx.Matches.Add(match);

        for (var i = 0; i < confirmedRegular; i++)
        {
            var p = MatchPresence.Create(match.Id, Guid.NewGuid(), PlayerType.Regular, now);
            p.Confirm(now);
            ctx.Presences.Add(p);
        }

        for (var i = 0; i < confirmedDropIn; i++)
        {
            var p = MatchPresence.Create(match.Id, Guid.NewGuid(), PlayerType.DropIn, now);
            p.Confirm(now);
            ctx.Presences.Add(p);
        }

        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return match.Id;
    }
}
