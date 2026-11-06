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
using Quadra.Modules.Matches.Persistence;

namespace Quadra.IntegrationTests.Geo;

/// <summary>
/// Linking a game to an arena (DECISIONS #92) through the real endpoints: the picked Google place
/// links it when the place belongs to an approved arena, the organizer's "É no …?" choice must be an
/// active arena, an edit keeps the arena unless the game moves, and <c>GET /arenas/near</c> offers
/// the approved arenas around a spot. Distance alone never links a game.
/// </summary>
public sealed class ArenaLinkEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    private static readonly Guid Organizer = Guid.NewGuid();

    private readonly MatchesWebApplicationFactory _fx;

    public ArenaLinkEndpointTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Creating_links_through_the_picked_place_or_the_chosen_arena_only()
    {
        const double lat = -23.60, lon = -46.60;
        var arena = await SaveArenaAsync(lat, lon, "p-create-a", active: true);
        var draft = await SaveArenaAsync(lat, lon, "p-create-draft", active: false);
        var merged = await SaveArenaAsync(lat, lon, "p-create-old", active: false, mergedInto: arena);
        var client = _fx.CreateAuthenticatedClient(Organizer);

        var byPlace = await CreateAsync(client, lat, lon, new { placeId = "p-create-a" });
        var nextDoor = await CreateAsync(client, lat, lon, new { placeId = "p-create-street" });
        var byChoice = await CreateAsync(client, lat, lon, new { arenaId = merged });
        var refused = await client.PostAsJsonAsync("/api/v1/matches", Body(lat, lon, new { arenaId = draft }), Ct);

        (await ArenaOfAsync(byPlace)).Should().Be(arena);
        (await ArenaOfAsync(nextDoor)).Should().BeNull("a place 0 m from an arena but not one of its entries links nothing");
        (await ArenaOfAsync(byChoice)).Should().Be(arena, "a merged arena links the one that stayed");
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest, "a draft arena is not on the map yet");
        (await refused.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("errors").TryGetProperty("ArenaId", out _)
            .Should().BeTrue();
    }

    [Fact]
    public async Task Editing_keeps_the_arena_relinks_on_a_new_place_and_drops_it_when_the_game_moves()
    {
        const double lat = -23.62, lon = -46.62;
        var first = await SaveArenaAsync(lat, lon, "p-edit-a", active: true);
        var second = await SaveArenaAsync(lat, lon, "p-edit-b", active: true);
        var client = _fx.CreateAuthenticatedClient(Organizer);
        var match = await CreateAsync(client, lat, lon, new { placeId = "p-edit-a" });

        await EditAsync(client, match, lat, lon, new { });
        (await ArenaOfAsync(match)).Should().Be(first, "an edit that does not touch the place keeps the arena");

        await EditAsync(client, match, lat, lon, new { placeId = "p-edit-b" });
        (await ArenaOfAsync(match)).Should().Be(second);

        await EditAsync(client, match, lat + 0.01, lon, new { });
        (await ArenaOfAsync(match)).Should().BeNull("the game moved away from its arena");
    }

    [Fact]
    public async Task Near_offers_the_linked_arena_first_then_approved_ones_within_100_m()
    {
        const double lat = -23.64, lon = -46.64;
        var here = await SaveArenaAsync(lat, lon, "p-near-here", active: true);
        var close = await SaveArenaAsync(lat + 0.00045, lon, "p-near-close", active: true); // ~50 m
        await SaveArenaAsync(lat, lon + 0.0004, "p-near-draft", active: false);
        var far = await SaveArenaAsync(lat + 0.0027, lon, "p-near-far", active: true); // ~300 m
        var client = _fx.CreateAuthenticatedClient(Organizer);

        var picked = await NearAsync(client, FormattableString.Invariant($"lat={lat}&lon={lon}&placeId=p-near-far"));
        var plain = await NearAsync(client, FormattableString.Invariant($"lat={lat}&lon={lon}"));

        picked.Select(i => i.GetProperty("id").GetGuid()).Should().Equal(far, here, close);
        picked[0].GetProperty("linked").GetBoolean().Should().BeTrue();
        picked[0].GetProperty("placeId").GetString().Should().Be("p-near-far");
        picked[1].GetProperty("linked").GetBoolean().Should().BeFalse();
        plain.Select(i => i.GetProperty("id").GetGuid()).Should().Equal(here, close);
        plain[1].GetProperty("distanceMeters").GetInt32().Should().BeInRange(45, 55);

        (await _fx.CreateAnonymousClient().GetAsync(FormattableString.Invariant($"/api/v1/arenas/near?lat={lat}&lon={lon}"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/arenas/near?lat=91&lon=0", Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static Dictionary<string, object?> Body(double lat, double lon, object extra)
    {
        var body = new Dictionary<string, object?>
        {
            ["name"] = "Racha da arena",
            ["address"] = "Rua Teste, 123",
            ["latitude"] = lat,
            ["longitude"] = lon,
            ["dateTime"] = DateTimeOffset.UtcNow.AddDays(7),
            ["maxPlayers"] = 12,
            ["regularSlots"] = 12,
            ["type"] = "OneOff",
        };
        foreach (var property in extra.GetType().GetProperties())
        {
            body[property.Name] = property.GetValue(extra);
        }

        return body;
    }

    private async Task<Guid> CreateAsync(HttpClient client, double lat, double lon, object extra)
    {
        var response = await client.PostAsJsonAsync("/api/v1/matches", Body(lat, lon, extra), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task EditAsync(HttpClient client, Guid matchId, double lat, double lon, object extra)
    {
        var body = Body(lat, lon, extra);
        body.Remove("type");
        var response = await client.PutAsJsonAsync($"/api/v1/matches/{matchId}", body, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<JsonElement[]> NearAsync(HttpClient client, string query)
    {
        var response = await client.GetAsync($"/api/v1/arenas/near?{query}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return [.. (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items").EnumerateArray()];
    }

    private async Task<Guid?> ArenaOfAsync(Guid matchId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Matches
            .Where(m => m.Id == matchId).Select(m => m.ArenaId).SingleAsync(Ct);
    }

    private async Task<Guid> SaveArenaAsync(double lat, double lon, string placeId, bool active, Guid? mergedInto = null)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
        var now = DateTimeOffset.UtcNow;
        var arena = Arena.Create(lat, lon, VenueKinds.Court, now);
        if (mergedInto is { } target)
        {
            arena.MergeInto(await db.Arenas.SingleAsync(a => a.Id == target, Ct), Organizer, now);
        }
        else if (active)
        {
            arena.Approve(Organizer, now);
        }

        db.Arenas.Add(arena);
        if (mergedInto is null)
        {
            db.ArenaPlaces.Add(ArenaPlace.Link(placeId, arena.Id, isPrimary: true, Organizer, now));
        }

        await db.SaveChangesAsync(Ct);
        return arena.Id;
    }
}
