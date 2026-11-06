using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Quadra.IntegrationTests.SecurityAudit;

namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>First-access tour: the owner records how they left it; only the owner reads it back.</summary>
public sealed class TourEndpointsTests : IClassFixture<AuditWebApplicationFactory>
{
    private const string Route = "/api/v1/profiles/me/tour";

    private readonly AuditWebApplicationFactory _fx;

    public TourEndpointsTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_player_has_no_tour_record()
    {
        var player = await _fx.LoginAsync();

        (await ProfileOf(player, "/api/v1/profiles/me")).GetProperty("tour").ValueKind
            .Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Owner_records_the_tour_and_a_later_record_replaces_it()
    {
        var player = await _fx.LoginAsync();

        var skipped = await player.Client.PutAsJsonAsync(Route, new { version = 1, outcome = "Skipped", lastStep = 1 }, Ct);
        skipped.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await skipped.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("outcome").GetString().Should().Be("Skipped");
        body.GetProperty("lastStep").GetInt32().Should().Be(1);

        (await player.Client.PutAsJsonAsync(Route, new { version = 1, outcome = "Completed", lastStep = 3 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var tour = (await ProfileOf(player, "/api/v1/profiles/me")).GetProperty("tour");
        tour.GetProperty("version").GetInt32().Should().Be(1);
        tour.GetProperty("outcome").GetString().Should().Be("Completed");
        tour.GetProperty("lastStep").GetInt32().Should().Be(3);
        tour.GetProperty("endedAt").ValueKind.Should().Be(JsonValueKind.String);
    }

    [Fact]
    public async Task Another_player_does_not_see_the_tour_record()
    {
        var owner = await _fx.LoginAsync();
        var other = await _fx.LoginAsync();
        (await owner.Client.PutAsJsonAsync(Route, new { version = 1, outcome = "Completed", lastStep = 3 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await ProfileOf(other, $"/api/v1/profiles/{owner.Id}")).GetProperty("tour").ValueKind
            .Should().Be(JsonValueKind.Null, "the tour record is the owner's business");
    }

    [Theory]
    [InlineData(0, "Completed", 3)]
    [InlineData(1, "Done", 3)]
    [InlineData(1, "Completed", -1)]
    public async Task Malformed_record_is_refused(int version, string outcome, int lastStep)
    {
        var player = await _fx.LoginAsync();

        (await player.Client.PutAsJsonAsync(Route, new { version, outcome, lastStep }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Anonymous_caller_is_refused()
    {
        using var anonymous = _fx.Factory.CreateClient();

        (await anonymous.PutAsJsonAsync(Route, new { version = 1, outcome = "Completed", lastStep = 3 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> ProfileOf(Actor player, string url)
    {
        var response = await player.Client.GetAsync(url, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }
}
