using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Rede safety: a player reports a post or blocks another player, and only the admins listed in
/// <c>Moderation:AdminUserIds</c> see the reports and delete posts.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class ModerationAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public ModerationAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_reported_post_leaves_the_reporters_feed_and_an_admin_deletes_it()
    {
        var author = await _fx.LoginAsync();
        var reporter = await _fx.LoginAsync();
        var bystander = await _fx.LoginAsync();
        var admin = await _fx.LoginAsync();
        var postId = await PostAsync(author, "post denunciado");

        (await reporter.Client.PostAsJsonAsync($"/api/v1/posts/{postId}/report", new { reason = "Offensive", details = "xingamento" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        // A second report by the same player changes nothing.
        (await reporter.Client.PostAsJsonAsync($"/api/v1/posts/{postId}/report", new { reason = "Spam" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await FeedIdsAsync(reporter)).Should().NotContain(postId);
        (await FeedIdsAsync(bystander)).Should().Contain(postId);

        // Only admins reach the moderation routes.
        (await reporter.Client.GetAsync("/api/v1/admin/reports", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await author.Client.DeleteAsync($"/api/v1/admin/posts/{postId}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var host = _fx.Create("Testing", new Dictionary<string, string?>
        {
            ["Moderation:AdminUserIds:0"] = admin.Id.ToString(),
        });
        var adminClient = host.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", admin.AccessToken);

        var reports = await Read(await adminClient.GetAsync("/api/v1/admin/reports", Ct));
        var reported = reports.EnumerateArray().Single(r => r.GetProperty("postId").GetGuid() == postId);
        reported.GetProperty("reportCount").GetInt32().Should().Be(1);
        reported.GetProperty("reports")[0].GetProperty("reason").GetString().Should().Be("Offensive");

        (await adminClient.DeleteAsync($"/api/v1/admin/posts/{postId}", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FeedIdsAsync(bystander)).Should().NotContain(postId);
        (await Read(await adminClient.GetAsync("/api/v1/admin/reports", Ct))).EnumerateArray()
            .Should().NotContain(r => r.GetProperty("postId").GetGuid() == postId);
    }

    [Fact]
    public async Task A_block_hides_both_players_from_each_other_until_undone()
    {
        var blocker = await _fx.LoginAsync();
        var blocked = await _fx.LoginAsync();
        (await blocked.Client.PutAsync($"/api/v1/profiles/{blocker.Id}/follow", null, Ct)).IsSuccessStatusCode.Should().BeTrue();
        var blockerPost = await PostAsync(blocker, "do bloqueador");
        var blockedPost = await PostAsync(blocked, "do bloqueado");
        (await blocked.Client.PostAsJsonAsync($"/api/v1/posts/{blockerPost}/comments", new { text = "oi" }, Ct))
            .IsSuccessStatusCode.Should().BeTrue();

        (await blocker.Client.PutAsync($"/api/v1/profiles/{blocked.Id}/block", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await FeedIdsAsync(blocker)).Should().NotContain(blockedPost);
        (await FeedIdsAsync(blocked)).Should().NotContain(blockerPost);
        (await Read(await blocker.Client.GetAsync($"/api/v1/posts/{blockerPost}/comments", Ct))).GetProperty("items")
            .GetArrayLength().Should().Be(0, "the blocked player's comment is hidden from the blocker");
        (await blocked.Client.PostAsJsonAsync($"/api/v1/posts/{blockerPost}/comments", new { text = "de novo" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await blocked.Client.PutAsync($"/api/v1/posts/{blockerPost}/like", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await blocked.Client.PostAsJsonAsync($"/api/v1/posts/{blockerPost}/report", new { reason = "Spam" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await blocked.Client.PutAsync($"/api/v1/profiles/{blocker.Id}/follow", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await Read(await blocked.Client.GetAsync($"/api/v1/profiles/{blocker.Id}/follow", Ct)))
            .GetProperty("followedByMe").GetBoolean().Should().BeFalse("blocking ends the follows both ways");

        var list = await Read(await blocker.Client.GetAsync("/api/v1/profiles/me/blocks", Ct));
        list.EnumerateArray().Select(b => b.GetProperty("userId").GetGuid()).Should().Equal(blocked.Id);

        (await blocker.Client.DeleteAsync($"/api/v1/profiles/{blocked.Id}/block", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await FeedIdsAsync(blocked)).Should().Contain(blockerPost);
        (await blocked.Client.PutAsync($"/api/v1/profiles/{blocker.Id}/follow", null, Ct)).IsSuccessStatusCode.Should().BeTrue();
    }

    [Fact]
    public async Task Invalid_moderation_requests_are_refused()
    {
        var user = await _fx.LoginAsync();
        var postId = await PostAsync(user, "meu");

        (await user.Client.PutAsync($"/api/v1/profiles/{user.Id}/block", null, Ct)).StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await user.Client.PutAsync($"/api/v1/profiles/{Guid.NewGuid()}/block", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await user.Client.PostAsJsonAsync($"/api/v1/posts/{postId}/report", new { reason = "NoSuchReason" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await user.Client.PostAsJsonAsync($"/api/v1/posts/{Guid.NewGuid()}/report", new { reason = "Spam" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var anon = _fx.Factory.CreateClient();
        (await anon.PostAsJsonAsync($"/api/v1/posts/{postId}/report", new { reason = "Spam" }, Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await anon.GetAsync("/api/v1/admin/reports", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static async Task<Guid> PostAsync(Actor author, string text) =>
        (await Read(await author.Client.PostAsJsonAsync("/api/v1/posts", new { text }, Ct), HttpStatusCode.Created))
            .GetProperty("id").GetGuid();

    private static async Task<IReadOnlyList<Guid>> FeedIdsAsync(Actor viewer) =>
        (await Read(await viewer.Client.GetAsync("/api/v1/posts?limit=50", Ct))).GetProperty("items").EnumerateArray()
            .Select(p => p.GetProperty("id").GetGuid()).ToList();

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
