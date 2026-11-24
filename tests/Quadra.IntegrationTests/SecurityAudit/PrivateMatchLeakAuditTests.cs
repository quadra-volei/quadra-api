using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Quadra.IntegrationTests.Modules.Auth;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// A match that becomes private stops reaching strangers: the post feed and the realtime room.
/// Found by the 2026-11-24 audit with the Cloudflare security-audit skill; each test asserts the secure behavior.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class PrivateMatchLeakAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public PrivateMatchLeakAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object MatchBody(string name, DateTimeOffset when, int max, int regular, string visibility = "Open") => new
    {
        name, address = "Rua Teste, 1", latitude = -23.56, longitude = -46.65, dateTime = when,
        maxPlayers = max, regularSlots = regular, type = "OneOff", visibility, inviteMode = visibility == "Private" ? "Code" : null,
        confirmationOpensHoursBefore = 96,
    };

    private async Task<Guid> CreateMatchAsync(Actor organizer, string name, int max = 12, int regular = 12, string visibility = "Open")
    {
        var r = await organizer.Client.PostAsJsonAsync("/api/v1/matches", MatchBody(name, DateTimeOffset.UtcNow.AddDays(2), max, regular, visibility), Ct);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync(Ct));
        return (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> MakePrivateAsync(Actor organizer, Guid matchId, string name) =>
        organizer.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}", new
        {
            name, description = (string?)null, address = "Rua Teste, 1", latitude = -23.56, longitude = -46.65,
            dateTime = DateTimeOffset.UtcNow.AddDays(2), maxPlayers = 12, regularSlots = 12, price = (decimal?)null,
            visibility = "Private",
        }, Ct);

    // 3. quadra-api:profile:posts-feed-private-match-exposure
    [Fact]
    public async Task Feed_hides_a_match_that_became_private()
    {
        var a = await _fx.LoginAsync();
        var stranger = await _fx.LoginAsync();
        var matchId = await CreateMatchAsync(a, "Racha que vira privado");
        var post = await a.Client.PostAsJsonAsync("/api/v1/posts", new { text = (string?)null, matchId }, Ct);
        post.IsSuccessStatusCode.Should().BeTrue(await post.Content.ReadAsStringAsync(Ct));
        (await (await stranger.Client.GetAsync("/api/v1/posts", Ct)).Content.ReadAsStringAsync(Ct))
            .Should().Contain(matchId.ToString(), "an open match shows in the feed");
        (await MakePrivateAsync(a, matchId, "Racha que vira privado")).IsSuccessStatusCode.Should().BeTrue();

        (await stranger.Client.GetAsync($"/api/v1/matches/{matchId}/detail", Ct)).StatusCode
            .Should().BeOneOf(HttpStatusCode.NotFound, HttpStatusCode.Forbidden);
        var feed = await (await stranger.Client.GetAsync("/api/v1/posts", Ct)).Content.ReadAsStringAsync(Ct);
        feed.Should().NotContain(matchId.ToString(), "the public feed still exposes a match that is now private");
    }

    // 4. quadra-api:realtime:match-room-stale-membership
    [Fact]
    public async Task Realtime_room_stops_after_the_match_becomes_private()
    {
        var a = await _fx.LoginAsync();
        var b = await _fx.LoginAsync();
        var p = await _fx.LoginAsync();
        var matchId = await CreateMatchAsync(a, "Sala que vira privada");

        var ws = _fx.Factory.Server.CreateWebSocketClient();
        using var socket = await ws.ConnectAsync(
            new Uri(_fx.Factory.Server.BaseAddress, $"hubs/match?access_token={b.AccessToken}".Replace("http", "ws")), Ct);
        await SendFrameAsync(socket, "{\"protocol\":\"json\",\"version\":1}");
        await ReceiveFramesAsync(socket, TimeSpan.FromSeconds(5));
        await SendFrameAsync(socket, $"{{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinMatchRoom\",\"arguments\":[\"{matchId}\"]}}");
        var join = await ReceiveFramesAsync(socket, TimeSpan.FromSeconds(5));
        join.Should().NotContain("\"error\"", "joining an open match room should work");

        (await MakePrivateAsync(a, matchId, "Sala que vira privada")).IsSuccessStatusCode.Should().BeTrue();
        var code = (await (await a.Client.GetAsync($"/api/v1/matches/{matchId}/detail", Ct)).Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("inviteCode").GetString();
        (await p.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed", inviteCode = code }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var pushed = await ReceiveFramesAsync(socket, TimeSpan.FromSeconds(5));
        pushed.Should().NotContain("PresenceUpdated", "a stranger kept receiving updates of a match after it became private");
    }

    private static Task SendFrameAsync(WebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json + "\u001e"), WebSocketMessageType.Text, true, Ct);

    private static async Task<string> ReceiveFramesAsync(WebSocket socket, TimeSpan window)
    {
        var sb = new StringBuilder();
        var buffer = new byte[16 * 1024];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(window);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
        }
        catch (OperationCanceledException)
        {
        }

        return sb.ToString();
    }
}
