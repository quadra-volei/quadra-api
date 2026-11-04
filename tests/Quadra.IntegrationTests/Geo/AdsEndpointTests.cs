using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quadra.IntegrationTests.Matches;
using Quadra.Modules.Geo.Persistence;

namespace Quadra.IntegrationTests.Geo;

/// <summary>
/// Self-service ads (DECISIONS #102) through the real endpoints: a player creates an ad at the
/// server's price; it shows to nobody until the team marks it paid and approves it; then the players
/// around its point see it, and each view and tap counts once per player and day. Only the owner
/// sees or cancels their ad, and only the team reaches the admin routes.
/// </summary>
public sealed class AdsEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    private const double Lat = -23.55, Lon = -46.63;
    private static readonly Guid Admin = Guid.NewGuid();
    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly MatchesWebApplicationFactory _fx;

    public AdsEndpointTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static string Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, Brasilia).DateTime).ToString("yyyy-MM-dd");

    private static object NewAd(string placement = "Home", double lat = Lat, double lon = Lon) => new
    {
        placement,
        lat,
        lon,
        radiusKm = 10,
        startsOn = Today,
        weeks = 1,
        title = "Arena Sol",
        body = "Day use a R$ 30",
        linkKind = "WhatsApp",
        linkValue = "5511999998888",
    };

    [Fact]
    public async Task An_ad_goes_on_the_air_only_after_payment_and_approval()
    {
        await ResetAsync();
        var advertiser = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var viewer = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var admin = AdminClient();

        var offer = await Read(await advertiser.GetAsync(FormattableString.Invariant($"/api/v1/ads/offer?lat={Lat}&lon={Lon}&radiusKm=10"), Ct));
        offer.GetProperty("placements").EnumerateArray().Select(p => p.GetProperty("placement").GetString())
            .Should().Equal("Home", "Map", "Opening");
        offer.GetProperty("paymentAvailable").GetBoolean().Should().BeFalse();

        var created = await advertiser.PostAsJsonAsync("/api/v1/ads", NewAd(), Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var ad = await Read(created);
        var id = ad.GetProperty("id").GetGuid();
        ad.GetProperty("phase").GetString().Should().Be("AwaitingPayment");
        ad.GetProperty("priceCents").GetInt32().Should().Be(5900, "the server sets the price");

        // Unpaid: nobody sees it, and the team cannot review it yet.
        (await viewer.GetAsync(Serve("Home"), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await admin.PostAsJsonAsync($"/api/v1/admin/ads/{id}/review", new { approve = true }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Paid but not approved: still nobody.
        (await admin.PostAsJsonAsync($"/api/v1/admin/ads/{id}/paid", new { note = "pix 123" }, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await viewer.GetAsync(Serve("Home"), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await advertiser.DeleteAsync($"/api/v1/ads/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict, "a paid ad is not cancelled in the app");

        // Approved: on the air around its point, not far away, not in another placement.
        var approved = await Read(await admin.PostAsJsonAsync($"/api/v1/admin/ads/{id}/review", new { approve = true }, Ct));
        approved.GetProperty("phase").GetString().Should().Be("Live");
        var served = await Read(await viewer.GetAsync(Serve("Home"), Ct));
        served.GetProperty("id").GetGuid().Should().Be(id);
        served.GetProperty("linkValue").GetString().Should().Be("5511999998888");
        (await viewer.GetAsync(Serve("Map"), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await viewer.GetAsync(Serve("Home", Lat + 0.5), Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent, "~55 km away");

        // A view and a tap count once per player and day.
        for (var i = 0; i < 2; i++)
        {
            (await viewer.PostAsJsonAsync($"/api/v1/ads/{id}/views", new { kind = "Impression" }, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        (await viewer.PostAsJsonAsync($"/api/v1/ads/{id}/views", new { kind = "Click" }, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await advertiser.PostAsJsonAsync($"/api/v1/ads/{id}/views", new { kind = "Impression" }, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await viewer.PostAsJsonAsync($"/api/v1/ads/{id}/views", new { kind = "Share" }, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var mine = (await Read(await advertiser.GetAsync("/api/v1/ads/mine", Ct))).EnumerateArray().Single();
        mine.GetProperty("impressions").GetInt32().Should().Be(2);
        mine.GetProperty("clicks").GetInt32().Should().Be(1);
    }

    [Fact]
    public async Task Only_the_owner_sees_and_cancels_an_ad_and_only_the_team_reviews()
    {
        var owner = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var stranger = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var id = (await Read(await owner.PostAsJsonAsync("/api/v1/ads", NewAd("Map"), Ct))).GetProperty("id").GetGuid();

        (await Read(await stranger.GetAsync("/api/v1/ads/mine", Ct))).GetArrayLength().Should().Be(0);
        (await stranger.DeleteAsync($"/api/v1/ads/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.GetAsync("/api/v1/admin/ads", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.PostAsJsonAsync($"/api/v1/admin/ads/{id}/paid", new { }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.PostAsJsonAsync($"/api/v1/admin/ads/{id}/review", new { approve = true }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await stranger.PostAsJsonAsync($"/api/v1/ads/{id}/views", new { kind = "Impression" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound, "an ad not on the air counts nothing");
        (await _fx.CreateAnonymousClient().GetAsync(Serve("Home"), Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        (await owner.DeleteAsync($"/api/v1/ads/{id}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await Read(await owner.GetAsync("/api/v1/ads/mine", Ct))).EnumerateArray().Single()
            .GetProperty("phase").GetString().Should().Be("Cancelled");
    }

    [Fact]
    public async Task Bad_requests_are_refused()
    {
        var owner = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        (await owner.PostAsJsonAsync("/api/v1/ads", new { placement = "Home", lat = Lat, lon = Lon, radiusKm = 10, startsOn = "2020-01-01", weeks = 1, title = "Velho" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "an ad cannot start in the past");
        (await owner.PostAsJsonAsync("/api/v1/ads", new { placement = "Home", lat = Lat, lon = Lon, radiusKm = 10, startsOn = Today, weeks = 1, title = "Foto", photoObjectKey = "profiles/someone-else/x.jpg" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "a photo must be the caller's upload");
        (await owner.PostAsJsonAsync("/api/v1/ads", new { placement = "Home", lat = Lat, lon = Lon, radiusKm = 10, startsOn = Today, weeks = 1, title = "Arena", linkKind = "Arena", linkValue = Guid.NewGuid().ToString() }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "the arena must be on the map");
    }

    private static string Serve(string placement, double lat = Lat) =>
        FormattableString.Invariant($"/api/v1/ads/serve?placement={placement}&lat={lat}&lon={Lon}");

    private HttpClient AdminClient()
    {
        var factory = _fx.Factory.WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Moderation:AdminUserIds:0"] = Admin.ToString(),
            })));
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = _fx.CreateAuthenticatedClient(Admin).DefaultRequestHeaders.Authorization;
        return client;
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response)
    {
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<GeoDbContext>().Database.ExecuteSqlRawAsync("TRUNCATE TABLE ad_views, ads;", Ct);
    }
}
