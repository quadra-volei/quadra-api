using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quadra.IntegrationTests.SecurityAudit;
using Quadra.Modules.Gamification.Abstractions;

namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>"Sua carta": the owner saves finish, title and card photo; everyone sees the result.</summary>
public sealed class CardStyleEndpointsTests : IClassFixture<AuditWebApplicationFactory>
{
    private const string Route = "/api/v1/profiles/me/card-style";

    private readonly AuditWebApplicationFactory _fx;

    public CardStyleEndpointsTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Owner_saves_the_style_and_another_player_sees_it_without_the_photo_key()
    {
        var owner = await _fx.LoginAsync();
        var other = await _fx.LoginAsync();

        var initial = await StyleOf(owner, "/api/v1/profiles/me");
        initial.GetProperty("finish").GetString().Should().Be("Prisma");
        initial.GetProperty("title").ValueKind.Should().Be(JsonValueKind.Null);

        var key = $"profiles/{owner.Id}/photo/{Guid.NewGuid()}.jpg";
        var saved = await owner.Client.PutAsJsonAsync(Route, new { finish = "Eclipse", title = "MAESTRO", photoObjectKey = key }, Ct);
        saved.StatusCode.Should().Be(HttpStatusCode.OK);

        var mine = await StyleOf(owner, "/api/v1/profiles/me");
        mine.GetProperty("finish").GetString().Should().Be("Eclipse");
        mine.GetProperty("title").GetString().Should().Be("MAESTRO");
        mine.GetProperty("photoObjectKey").GetString().Should().Be(key);

        var seen = await StyleOf(other, $"/api/v1/profiles/{owner.Id}");
        seen.GetProperty("finish").GetString().Should().Be("Eclipse");
        seen.GetProperty("title").GetString().Should().Be("MAESTRO");
        seen.GetProperty("photoObjectKey").ValueKind.Should().Be(JsonValueKind.Null, "the key is the owner's business");
    }

    [Theory]
    [InlineData("Gold", null)]
    [InlineData("1", null)]
    [InlineData(null, null)]
    [InlineData("Prisma", "O REI")]
    public async Task Unknown_finish_or_title_is_refused(string? finish, string? title)
    {
        var owner = await _fx.LoginAsync();

        (await owner.Client.PutAsJsonAsync(Route, new { finish, title }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_photo_owned_by_someone_else_is_refused()
    {
        var owner = await _fx.LoginAsync();
        var victim = await _fx.LoginAsync();

        var response = await owner.Client.PutAsJsonAsync(
            Route, new { finish = "Prisma", photoObjectKey = $"profiles/{victim.Id}/photo/x.jpg" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_season_pass_title_and_finish_are_equipped_only_once_unlocked()
    {
        var owner = await _fx.LoginAsync();
        var title = new { finish = "Prisma", title = "SEMPRE_PRESENTE" };
        var finish = new { finish = "Pioneira", title = (string?)null };

        (await owner.Client.PutAsJsonAsync(Route, title, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await owner.Client.PutAsJsonAsync(Route, finish, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Four weeks over the cap (62 points each): 240 points, the pass's last level.
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<IMatchPointsWriter>();
            for (var week = 0; week < 4; week++)
            {
                var when = DateTimeOffset.UtcNow.AddDays(-7 * week);
                await writer.ApplyFinishedMatchPointsAsync(
                    new FinishedMatchPoints(
                        Guid.NewGuid(),
                        Guid.NewGuid(),
                        when,
                        owner.Id,
                        [new FinishedMatchPlayerPoints(owner.Id, GamesWon: 3, MvpVotes: 2)],
                        when,
                        MvpTotalVotes: 3),
                    Ct);
            }
        }

        (await owner.Client.PutAsJsonAsync(Route, title, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await owner.Client.PutAsJsonAsync(Route, finish, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await StyleOf(owner, "/api/v1/profiles/me")).GetProperty("finish").GetString().Should().Be("Pioneira");
    }

    [Fact]
    public async Task Anonymous_caller_is_refused()
    {
        using var anonymous = _fx.Factory.CreateClient();

        (await anonymous.PutAsJsonAsync(Route, new { finish = "Prisma" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<JsonElement> StyleOf(Actor player, string url)
    {
        var response = await player.Client.GetAsync(url, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        return body.GetProperty("cardStyle");
    }
}
