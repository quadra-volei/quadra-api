using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Who may see a match (CanViewMatch; audit SEC-02, SEC-04, SEC-05). Actors: the organizer A of a
/// PRIVATE (invite-code) recurring match that was fully played (teams, scoreboard, MVP, summary,
/// ranking); P, a player who joined with the code; and B, a logged-in stranger with no relation.
/// Each test asserts the secure behavior.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class MatchVisibilityAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Scenario? _scenario;

    public MatchVisibilityAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record Scenario(Actor A, Actor P, Actor P2, Actor B, Guid MatchId, string InviteCode);

    private async Task<Scenario> GetScenarioAsync()
    {
        await Gate.WaitAsync(Ct);
        try
        {
            return _scenario ??= await BuildAsync();
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task<Scenario> BuildAsync()
    {
        var a = await _fx.LoginAsync();
        var p = await _fx.LoginAsync();
        var p2 = await _fx.LoginAsync();
        var b = await _fx.LoginAsync();

        // Within the kickoff tolerance (SEC-06), so the game can be played now.
        var when = DateTimeOffset.UtcNow.AddMinutes(10);
        var created = await Json(await a.Client.PostAsJsonAsync("/api/v1/matches", new
        {
            name = "Racha secreto",
            address = "Rua Privada, 123",
            latitude = -23.561,
            longitude = -46.656,
            dateTime = when,
            maxPlayers = 12,
            regularSlots = 12,
            type = "Recurring",
            frequency = "Weekly",
            dayOfWeek = ((int)when.DayOfWeek + 6) % 7 + 1,
            visibility = "Private",
            inviteMode = "Code",
            confirmationOpensHoursBefore = 96,
        }, Ct), HttpStatusCode.Created);
        var matchId = created.GetProperty("id").GetGuid();
        var base_ = $"/api/v1/matches/{matchId}";

        var code = (await Json(await a.Client.GetAsync($"{base_}/detail", Ct), HttpStatusCode.OK))
            .GetProperty("inviteCode").GetString()!;
        foreach (var who in new[] { a, p, p2 })
        {
            await Json(await who.Client.PutAsJsonAsync($"{base_}/presences/me", new { status = "Confirmed", inviteCode = code }, Ct),
                HttpStatusCode.OK);
        }

        var teams = (await Json(await a.Client.PostAsJsonAsync($"{base_}/teams/draft", new { teamCount = 2, mode = "Random" }, Ct),
            HttpStatusCode.OK)).GetProperty("teams").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToArray();
        await Json(await a.Client.PostAsJsonAsync($"{base_}/scoreboard", new { format = "BestOf3", teamAId = teams[0], teamBId = teams[1] }, Ct), HttpStatusCode.Created);
        await Json(await a.Client.PostAsync($"{base_}/scoreboard/start", null, Ct), HttpStatusCode.OK);
        JsonElement last = default;
        for (var set = 1; set <= 2; set++)
        {
            for (var i = 0; i < 25; i++)
            {
                last = await Json(await a.Client.PostAsJsonAsync($"{base_}/scoreboard/sets/{set}/points", new { teamId = teams[0] }, Ct),
                    HttpStatusCode.OK);
            }
        }

        last.GetProperty("state").GetString().Should().Be("Ended");
        // Ending the scoreboard may already open the MVP voting; opening it again answers 409.
        (await a.Client.PostAsJsonAsync($"{base_}/mvp-voting", new { }, Ct)).StatusCode
            .Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
        await Json(await p.Client.PostAsJsonAsync($"{base_}/mvp-voting/votes", new { votedPlayerId = p2.Id }, Ct), HttpStatusCode.OK);
        await Json(await a.Client.PostAsync($"{base_}/mvp-voting/close", null, Ct), HttpStatusCode.OK);
        var summary = await a.Client.PostAsync($"{base_}/summary", null, Ct);
        summary.IsSuccessStatusCode.Should().BeTrue(await summary.Content.ReadAsStringAsync(Ct));

        return new Scenario(a, p, p2, b, matchId, code);
    }

    // ── R2: private match data read by a stranger ─────────────────────────────

    [Fact]
    public async Task R2_stranger_does_not_see_private_match_in_list()
    {
        var s = await GetScenarioAsync();
        var all = await Json(await s.B.Client.GetAsync("/api/v1/matches?page=1&pageSize=100", Ct), HttpStatusCode.OK);
        var byOrganizer = await Json(await s.B.Client.GetAsync($"/api/v1/matches?organizerId={s.A.Id}", Ct), HttpStatusCode.OK);

        Ids(all).Should().NotContain(s.MatchId, "GET /matches must not list a private match to a stranger");
        Ids(byOrganizer).Should().NotContain(s.MatchId, "filtering by organizerId must not reveal private matches");
    }

    [Theory]
    [InlineData("")]
    [InlineData("/detail")]
    [InlineData("/presences")]
    public async Task R2_stranger_cannot_read_private_match(string suffix)
    {
        var s = await GetScenarioAsync();
        var response = await s.B.Client.GetAsync($"/api/v1/matches/{s.MatchId}{suffix}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        response.StatusCode.Should().BeOneOf([HttpStatusCode.NotFound, HttpStatusCode.Forbidden],
            $"a stranger read GET /matches/{{id}}{suffix} of a private match; body leaks: {Leak(body, s)}");
    }

    [Fact]
    public async Task R2_control_participant_can_read_private_match_detail()
    {
        var s = await GetScenarioAsync();
        (await s.P.Client.GetAsync($"/api/v1/matches/{s.MatchId}/detail", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task R2_invite_code_is_returned_only_to_the_organizer()
    {
        var s = await GetScenarioAsync();
        var asParticipant = await Json(await s.P.Client.GetAsync($"/api/v1/matches/{s.MatchId}/detail", Ct), HttpStatusCode.OK);
        asParticipant.GetProperty("inviteCode").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── R4: game data of a private match read by a stranger ──────────────────

    [Theory]
    [InlineData("/teams")]
    [InlineData("/scoreboard")]
    [InlineData("/mvp-voting")]
    [InlineData("/summary")]
    [InlineData("/ranking")]
    public async Task R4_stranger_cannot_read_private_game_data(string suffix)
    {
        var s = await GetScenarioAsync();
        var response = await s.B.Client.GetAsync($"/api/v1/matches/{s.MatchId}{suffix}", Ct);
        response.StatusCode.Should().BeOneOf([HttpStatusCode.NotFound, HttpStatusCode.Forbidden],
            $"a stranger read {suffix} of a private match: {Truncate(await response.Content.ReadAsStringAsync(Ct))}");
    }

    // ── R3: realtime room of a private match ─────────────────────────────────

    [Fact]
    public async Task R3_stranger_cannot_listen_to_a_private_match_room()
    {
        var s = await GetScenarioAsync();

        // A fresh private match so a presence change happens after the stranger joins the room.
        var when = DateTimeOffset.UtcNow.AddDays(2);
        var match = await Json(await s.A.Client.PostAsJsonAsync("/api/v1/matches", new
        {
            name = "Outro racha secreto", address = "Rua X", latitude = -23.5, longitude = -46.6,
            dateTime = when, maxPlayers = 12, regularSlots = 12, type = "OneOff",
            visibility = "Private", inviteMode = "Code", confirmationOpensHoursBefore = 72,
        }, Ct), HttpStatusCode.Created);
        var matchId = match.GetProperty("id").GetGuid();
        var code = (await Json(await s.A.Client.GetAsync($"/api/v1/matches/{matchId}/detail", Ct), HttpStatusCode.OK))
            .GetProperty("inviteCode").GetString();

        var ws = _fx.Factory.Server.CreateWebSocketClient();
        using var socket = await ws.ConnectAsync(
            new Uri(_fx.Factory.Server.BaseAddress, $"hubs/match?access_token={s.B.AccessToken}".Replace("http", "ws")), Ct);
        await SendFrameAsync(socket, "{\"protocol\":\"json\",\"version\":1}");
        await ReceiveFramesAsync(socket, TimeSpan.FromSeconds(5)); // handshake ack
        await SendFrameAsync(socket,
            $"{{\"type\":1,\"invocationId\":\"1\",\"target\":\"JoinMatchRoom\",\"arguments\":[\"{matchId}\"]}}");
        var joinReply = await ReceiveFramesAsync(socket, TimeSpan.FromSeconds(5));

        await Json(await s.P.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me",
            new { status = "Confirmed", inviteCode = code }, Ct), HttpStatusCode.OK);
        var pushed = await ReceiveFramesAsync(socket, TimeSpan.FromSeconds(5));

        pushed.Should().NotContain("PresenceUpdated",
            $"a stranger joined the room of a private match (join reply: {Truncate(joinReply)}) and received: {Truncate(pushed)}");
    }

    // ── Another player's history does not reveal private matches ─────────────

    [Fact]
    public async Task Stranger_does_not_see_private_match_name_in_someones_history()
    {
        var s = await GetScenarioAsync();

        var asStranger = await Json(await s.B.Client.GetAsync($"/api/v1/profiles/{s.P.Id}/match-history", Ct), HttpStatusCode.OK);
        var asTeammate = await Json(await s.P2.Client.GetAsync($"/api/v1/profiles/{s.P.Id}/match-history", Ct), HttpStatusCode.OK);

        var hidden = asStranger.GetProperty("items").EnumerateArray().Single();
        hidden.GetProperty("matchName").GetString().Should().Be("Partida privada");
        hidden.GetProperty("matchId").GetGuid().Should().Be(Guid.Empty);
        asStranger.ToString().Should().NotContain("Racha secreto").And.NotContain(s.MatchId.ToString());
        asTeammate.GetProperty("items").EnumerateArray().Single().GetProperty("matchName").GetString().Should().Be("Racha secreto");
    }

    // ── Controls: write endpoints are organizer-only, private join needs the code ──

    public static TheoryData<string, string, string?> StrangerWrites() => new()
    {
        { "DELETE", "", null },
        { "POST", "/guests", "{\"name\":\"Intruso\"}" },
        { "POST", "/teams/draft", "{}" },
        { "POST", "/scoreboard", "{\"format\":\"BestOf3\"}" },
        { "POST", "/scoreboard/start", null },
        { "POST", "/scoreboard/end", null },
        { "POST", "/mvp-voting", "{}" },
        { "POST", "/mvp-voting/close", null },
        { "POST", "/summary", null },
    };

    [Theory]
    [MemberData(nameof(StrangerWrites))]
    public async Task Control_stranger_cannot_write_to_someone_elses_match(string method, string suffix, string? body)
    {
        var s = await GetScenarioAsync();
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/matches/{s.MatchId}{suffix}");
        if (body is not null)
        {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var response = await s.B.Client.SendAsync(request, Ct);
        response.IsSuccessStatusCode.Should().BeFalse($"{method} {suffix} by a stranger succeeded");
        response.StatusCode.Should().BeOneOf(
            [HttpStatusCode.Forbidden, HttpStatusCode.NotFound, HttpStatusCode.Conflict, HttpStatusCode.BadRequest]);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("WRONGCDE")]
    public async Task Control_stranger_cannot_join_private_match_without_the_code(string? code)
    {
        var s = await GetScenarioAsync();
        var when = DateTimeOffset.UtcNow.AddDays(2);
        var match = await Json(await s.A.Client.PostAsJsonAsync("/api/v1/matches", new
        {
            name = "Privada", address = "Rua Z", latitude = -23.5, longitude = -46.6, dateTime = when,
            maxPlayers = 12, regularSlots = 12, type = "OneOff", visibility = "Private", inviteMode = "Code",
            confirmationOpensHoursBefore = 72,
        }, Ct), HttpStatusCode.Created);
        (await s.B.Client.PutAsJsonAsync($"/api/v1/matches/{match.GetProperty("id").GetGuid()}/presences/me",
            new { status = "Confirmed", inviteCode = code }, Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Control_stranger_cannot_vote_mvp_in_someone_elses_game()
    {
        var s = await GetScenarioAsync();
        var response = await s.B.Client.PostAsJsonAsync($"/api/v1/matches/{s.MatchId}/mvp-voting/votes",
            new { votedPlayerId = s.P.Id }, Ct);
        response.IsSuccessStatusCode.Should().BeFalse();
    }

    // ── Profiles and Rede ────────────────────────────────────────────────────

    [Fact]
    public async Task Control_other_profile_hides_private_fields_and_auth_me_is_own()
    {
        var s = await GetScenarioAsync();
        var other = await s.B.Client.GetAsync($"/api/v1/profiles/{s.A.Id}", Ct);
        if (other.StatusCode == HttpStatusCode.OK)
        {
            var json = await other.Content.ReadFromJsonAsync<JsonElement>(Ct);
            json.GetProperty("birthDate").ValueKind.Should().Be(JsonValueKind.Null);
            json.GetProperty("photoObjectKey").ValueKind.Should().Be(JsonValueKind.Null);
            json.ToString().Should().NotContain(s.A.Phone);
        }

        var me = await Json(await s.B.Client.GetAsync("/api/v1/auth/me", Ct), HttpStatusCode.OK);
        me.GetProperty("userId").GetGuid().Should().Be(s.B.Id);
    }

    [Fact]
    public async Task Control_stranger_cannot_delete_someone_elses_post_or_comment()
    {
        var s = await GetScenarioAsync();
        var post = await Json(await s.A.Client.PostAsJsonAsync("/api/v1/posts", new { text = "post do A" }, Ct), HttpStatusCode.Created);
        var postId = post.GetProperty("id").GetGuid();
        var comment = await Json(await s.P.Client.PostAsJsonAsync($"/api/v1/posts/{postId}/comments", new { text = "oi" }, Ct),
            HttpStatusCode.Created);

        (await s.B.Client.DeleteAsync($"/api/v1/posts/{postId}", Ct)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await s.B.Client.DeleteAsync($"/api/v1/posts/{postId}/comments/{comment.GetProperty("id").GetGuid()}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Static: no controller action is anonymous by accident ────────────────

    [Fact]
    public void Every_controller_action_requires_authentication_unless_whitelisted()
    {
        string[] allowedAnonymous =
        [
            "AuthController.LoginSmsOtp", "AuthController.LoginGoogle", "AuthController.LoginApple",
            "AuthController.LoginFirebasePhone",
            "AuthController.Refresh", "AuthController.Logout",
            "AppVersionController.Get", // public version gate, read before login (DECISIONS #74)
        ];
        var controllers = AppDomain.CurrentDomain.GetAssemblies()
            .Where(asm => asm.GetName().Name?.StartsWith("Quadra.", StringComparison.Ordinal) == true)
            .SelectMany(asm => asm.GetTypes())
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract && t.Namespace?.Contains("Tests") != true);

        var anonymous = new List<string>();
        var unprotected = new List<string>();
        foreach (var type in controllers)
        {
            var classAuth = type.GetCustomAttribute<AuthorizeAttribute>() is not null;
            foreach (var action in type.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                         .Where(m => m.GetCustomAttributes().Any(a => a is Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute)))
            {
                var name = $"{type.Name}.{action.Name}";
                if (action.GetCustomAttribute<AllowAnonymousAttribute>() is not null || type.GetCustomAttribute<AllowAnonymousAttribute>() is not null)
                {
                    anonymous.Add(name);
                }
                else if (!classAuth && action.GetCustomAttribute<AuthorizeAttribute>() is null)
                {
                    unprotected.Add(name);
                }
            }
        }

        unprotected.Should().BeEmpty();
        anonymous.Should().BeEquivalentTo(allowedAnonymous);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(expected, text);
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static IEnumerable<Guid> Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(m => m.GetProperty("id").GetGuid());

    private static string Leak(string body, Scenario s)
    {
        var leaks = new List<string>();
        if (body.Contains("Rua Privada")) leaks.Add("endereço");
        if (body.Contains("-23.561")) leaks.Add("coordenadas");
        if (body.Contains(s.P.Id.ToString())) leaks.Add("id de participante");
        if (body.Contains(s.InviteCode)) leaks.Add("CÓDIGO DE CONVITE");
        return leaks.Count == 0 ? "(nada identificado)" : string.Join(", ", leaks);
    }

    private static string Truncate(string text) => text.Length > 300 ? text[..300] + "…" : text;

    private static Task SendFrameAsync(WebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json + "\u001e"), WebSocketMessageType.Text, true, Ct);

    private static async Task<string> ReceiveFramesAsync(WebSocket socket, TimeSpan wait)
    {
        var buffer = new byte[16 * 1024];
        var text = new StringBuilder();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(wait);
        try
        {
            do
            {
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                if (result.EndOfMessage && text.ToString().Contains("PresenceUpdated"))
                {
                    break;
                }

                if (result.EndOfMessage && !text.ToString().Contains("\"type\":6"))
                {
                    break;
                }
            }
            while (true);
        }
        catch (OperationCanceledException)
        {
        }

        return text.ToString();
    }
}
