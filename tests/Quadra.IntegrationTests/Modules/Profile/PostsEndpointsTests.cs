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
            .Database.ExecuteSqlRawAsync("TRUNCATE TABLE posts CASCADE;", Ct));
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
            Post.Create(author, "primeiro", null, null, null, null, now.AddMinutes(-3)),
            Post.Create(author, "segundo", null, null, null, null, now.AddMinutes(-2)),
            Post.Create(author, "terceiro", null, null, null, null, now.AddMinutes(-1)));
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
        var post = Post.Create(author, "meu", null, null, null, null, DateTimeOffset.UtcNow);
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
            .Select(i => (object)Post.Create(author, $"post {i}", null, null, null, null, DateTimeOffset.UtcNow.AddMinutes(-i)))
            .ToArray());

        (await _fx.CreateAuthenticatedClient(author).PostAsJsonAsync(Url, new { text = "mais um" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    [Fact]
    public async Task Liking_is_per_player_idempotent_and_shows_in_the_feed()
    {
        await ResetAsync();
        var author = Guid.NewGuid();
        var fan = Guid.NewGuid();
        var post = Post.Create(author, "curte aí", null, null, null, null, DateTimeOffset.UtcNow);
        await SeedAsync(post);
        var likeUrl = $"{Url}/{post.Id}/like";
        var fanClient = _fx.CreateAuthenticatedClient(fan);

        (await fanClient.PutAsync(likeUrl, null, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        var again = await (await fanClient.PutAsync(likeUrl, null, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        again.GetProperty("likeCount").GetInt32().Should().Be(1);
        again.GetProperty("likedByMe").GetBoolean().Should().BeTrue();
        await _fx.CreateAuthenticatedClient(author).PutAsync(likeUrl, null, Ct);

        var fanView = (await fanClient.GetFromJsonAsync<JsonElement>(Url, Ct)).GetProperty("items")[0];
        fanView.GetProperty("likeCount").GetInt32().Should().Be(2);
        fanView.GetProperty("likedByMe").GetBoolean().Should().BeTrue();
        var strangerView = (await _fx.CreateAuthenticatedClient(Guid.NewGuid())
            .GetFromJsonAsync<JsonElement>(Url, Ct)).GetProperty("items")[0];
        strangerView.GetProperty("likeCount").GetInt32().Should().Be(2);
        strangerView.GetProperty("likedByMe").GetBoolean().Should().BeFalse();

        var unliked = await (await fanClient.DeleteAsync(likeUrl, Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct);
        unliked.GetProperty("likeCount").GetInt32().Should().Be(1);
        unliked.GetProperty("likedByMe").GetBoolean().Should().BeFalse();

        (await fanClient.PutAsync($"{Url}/{Guid.NewGuid()}/like", null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Deleting the post takes its likes along.
        (await _fx.CreateAuthenticatedClient(author).DeleteAsync($"{Url}/{post.Id}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        await _fx.WithScopeAsync(async sp =>
            (await sp.GetRequiredService<ProfileDbContext>().PostLikes.CountAsync(Ct)).Should().Be(0));
    }

    [Fact]
    public async Task Comments_are_listed_oldest_first_counted_in_the_feed_and_deleted_by_their_author_or_the_posts()
    {
        await ResetAsync();
        var author = Guid.NewGuid();
        var fan = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var post = Post.Create(author, "comenta aí", null, null, null, null, DateTimeOffset.UtcNow);
        await SeedAsync(post);
        var url = $"{Url}/{post.Id}/comments";
        var fanClient = _fx.CreateAuthenticatedClient(fan);

        var first = await fanClient.PostAsJsonAsync(url, new { text = "  Bom jogo!  " }, Ct);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstId = (await first.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var second = await _fx.CreateAuthenticatedClient(stranger).PostAsJsonAsync(url, new { text = "Semana que vem tem mais" }, Ct);
        var secondId = (await second.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();

        (await fanClient.PostAsJsonAsync(url, new { text = "   " }, Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await fanClient.PostAsJsonAsync(url, new { text = new string('a', PostComment.TextMaxLength + 1) }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await fanClient.PostAsJsonAsync($"{Url}/{Guid.NewGuid()}/comments", new { text = "oi" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var listed = (await fanClient.GetFromJsonAsync<JsonElement>(url, Ct)).GetProperty("items");
        listed.EnumerateArray().Select(c => c.GetProperty("text").GetString())
            .Should().Equal("Bom jogo!", "Semana que vem tem mais");
        // The fan may delete their own comment, not the stranger's.
        listed[0].GetProperty("canDelete").GetBoolean().Should().BeTrue();
        listed[1].GetProperty("canDelete").GetBoolean().Should().BeFalse();
        (await fanClient.GetFromJsonAsync<JsonElement>(Url, Ct)).GetProperty("items")[0]
            .GetProperty("commentCount").GetInt32().Should().Be(2);

        (await fanClient.DeleteAsync($"{url}/{secondId}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await fanClient.DeleteAsync($"{url}/{firstId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        // The post's author moderates their own post.
        (await _fx.CreateAuthenticatedClient(author).DeleteAsync($"{url}/{secondId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await fanClient.DeleteAsync($"{url}/{firstId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await fanClient.GetFromJsonAsync<JsonElement>(url, Ct)).GetProperty("items").GetArrayLength().Should().Be(0);
    }

    private static IEnumerable<string?> Texts(JsonElement feed) =>
        feed.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("text").GetString());
}
