using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;
using Quadra.Modules.Profile.Posts;

namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>
/// HTTP tests for <c>/api/v1/posts</c> against a real Postgres: a post is stored and read back
/// in the feed with its author, photo and match; only a played match and an own photo are
/// accepted; only the author deletes.
/// </summary>
[Collection("Profile")]
public sealed class PostsEndpointsTests
{
    private const string Url = "/api/v1/posts";
    private readonly ProfileWebApplicationFactory _fx;

    public PostsEndpointsTests(ProfileWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task ResetAsync()
    {
        await _fx.ResetAsync();
        await _fx.WithScopeAsync(sp => sp.GetRequiredService<ProfileDbContext>()
            .Database.ExecuteSqlRawAsync("TRUNCATE TABLE posts;", Ct));
    }

    private Task SeedAsync(params object[] rows) =>
        _fx.WithScopeAsync(async sp =>
        {
            var ctx = sp.GetRequiredService<ProfileDbContext>();
            ctx.AddRange(rows);
            await ctx.SaveChangesAsync(Ct);
        });

    private static PlayerMatchHistoryEntry Played(Guid userId, Guid matchId, string name) =>
        PlayerMatchHistoryEntry.Create(
            userId, matchId, name, DateTimeOffset.UtcNow.AddDays(-1), teamId: null,
            MatchOutcome.Win, wasMvp: false, durationSeconds: null,
            recordedAt: DateTimeOffset.UtcNow, now: DateTimeOffset.UtcNow);

    [Fact]
    public async Task A_post_with_photo_and_match_shows_up_in_the_feed()
    {
        await ResetAsync();
        var author = Guid.NewGuid();
        var matchId = Guid.NewGuid();
        var profile = PlayerProfile.Provision(author, DateTimeOffset.UtcNow);
        profile.UpdateDetails("Ana", "Lima", "ana", new DateOnly(1995, 1, 1), PlayerPosition.LEV, null, DateTimeOffset.UtcNow);
        await SeedAsync(profile, Played(author, matchId, "Vôlei de quinta"));
        var photoKey = $"profiles/{author}/photo/abc.jpg";

        var created = await _fx.CreateAuthenticatedClient(author).PostAsJsonAsync(
            Url, new { text = "  Que jogo!  ", photoObjectKey = photoKey, matchId }, Ct);

        created.StatusCode.Should().Be(HttpStatusCode.Created);

        var reader = Guid.NewGuid();
        var feed = await _fx.CreateAuthenticatedClient(reader).GetFromJsonAsync<JsonElement>(Url, Ct);
        var item = feed.GetProperty("items").EnumerateArray().Should().ContainSingle().Subject;
        item.GetProperty("text").GetString().Should().Be("Que jogo!");
        item.GetProperty("photoUrl").GetString().Should().Be($"https://s3.test.local/read/{photoKey}");
        item.GetProperty("author").GetProperty("displayName").GetString().Should().Be("Ana Lima");
        item.GetProperty("author").GetProperty("handle").GetString().Should().Be("ana");
        item.GetProperty("match").GetProperty("id").GetGuid().Should().Be(matchId);
        item.GetProperty("match").GetProperty("name").GetString().Should().Be("Vôlei de quinta");
        item.GetProperty("mine").GetBoolean().Should().BeFalse();
        feed.GetProperty("nextBefore").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_feed_is_newest_first_and_pages_with_before()
    {
        await ResetAsync();
        var author = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await SeedAsync(
            Post.Create(author, "primeiro", null, null, null, now.AddMinutes(-3)),
            Post.Create(author, "segundo", null, null, null, now.AddMinutes(-2)),
            Post.Create(author, "terceiro", null, null, null, now.AddMinutes(-1)));
        var client = _fx.CreateAuthenticatedClient(author);

        var first = await client.GetFromJsonAsync<JsonElement>($"{Url}?limit=2", Ct);
        Texts(first).Should().Equal("terceiro", "segundo");
        first.GetProperty("items")[0].GetProperty("mine").GetBoolean().Should().BeTrue();

        var before = Uri.EscapeDataString(first.GetProperty("nextBefore").GetString()!);
        var second = await client.GetFromJsonAsync<JsonElement>($"{Url}?limit=2&before={before}", Ct);
        Texts(second).Should().Equal("primeiro");
        second.GetProperty("nextBefore").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task POST_refuses_an_empty_post_a_foreign_photo_and_a_match_not_played()
    {
        await ResetAsync();
        var author = Guid.NewGuid();
        var client = _fx.CreateAuthenticatedClient(author);

        (await client.PostAsJsonAsync(Url, new { text = "   " }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(Url, new { text = new string('a', Post.TextMaxLength + 1) }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await client.PostAsJsonAsync(Url, new { photoObjectKey = $"profiles/{Guid.NewGuid()}/photo/x.jpg" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await client.PostAsJsonAsync(Url, new { text = "oi", matchId = Guid.NewGuid() }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await _fx.CreateAnonymousClient().PostAsJsonAsync(Url, new { text = "oi" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        await _fx.WithScopeAsync(async sp =>
            (await sp.GetRequiredService<ProfileDbContext>().Posts.CountAsync(Ct)).Should().Be(0));
    }

    [Fact]
    public async Task DELETE_is_only_for_the_author()
    {
        await ResetAsync();
        var author = Guid.NewGuid();
        var post = Post.Create(author, "meu", null, null, null, DateTimeOffset.UtcNow);
        await SeedAsync(post);

        (await _fx.CreateAuthenticatedClient(Guid.NewGuid()).DeleteAsync($"{Url}/{post.Id}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await _fx.CreateAuthenticatedClient(author).DeleteAsync($"{Url}/{post.Id}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await _fx.CreateAuthenticatedClient(author).DeleteAsync($"{Url}/{post.Id}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task POST_over_the_daily_limit_returns_429()
    {
        await ResetAsync();
        var author = Guid.NewGuid();
        await SeedAsync(Enumerable.Range(0, PostsHandler.DailyLimit)
            .Select(i => (object)Post.Create(author, $"post {i}", null, null, null, DateTimeOffset.UtcNow.AddMinutes(-i)))
            .ToArray());

        (await _fx.CreateAuthenticatedClient(author).PostAsJsonAsync(Url, new { text = "mais um" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private static IEnumerable<string?> Texts(JsonElement feed) =>
        feed.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("text").GetString());
}
