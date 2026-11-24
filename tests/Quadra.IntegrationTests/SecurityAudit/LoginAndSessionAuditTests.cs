using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Quadra.IntegrationTests.Modules.Auth;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Oidc;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Phone login and sessions: fixed code only in Development/Testing, logout ends the session it names, bad tokens cannot lock others out.
/// Found by the 2026-11-24 audit with the Cloudflare security-audit skill; each test asserts the secure behavior.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class LoginAndSessionAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public LoginAndSessionAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

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

    // 1. quadra-api:auth:fake-phone-provider-deny-list-gate
    [Fact]
    public async Task Fixed_code_login_is_refused_outside_development_and_testing()
    {
        var victim = await _fx.LoginAsync();
        HttpResponseMessage? response = null;
        try
        {
            using var staging = _fx.Create("Staging", new Dictionary<string, string?>());
            response = await staging.CreateClient().PostAsJsonAsync("/api/v1/auth/login/sms-otp",
                new { step = "verify", phoneNumber = victim.Phone, code = TestJwtFactory.FakeOtpCode }, Ct);
        }
        catch (Exception e) when (e is not Xunit.Sdk.XunitException)
        {
            return; // host refused to start with the Fake provider: secure
        }

        var body = await response.Content.ReadAsStringAsync(Ct);
        body.Should().NotContain(victim.Id.ToString(),
            "a Staging host with the Fake provider let an anonymous caller log into an existing account with the fixed code");
    }

    // quadra-api:auth:logout-refresh-race-leaves-session-active (logout with the token held before a refresh)
    [Fact]
    public async Task Logout_with_an_already_rotated_token_ends_the_session()
    {
        var user = await _fx.LoginAsync();
        var client = _fx.Factory.CreateClient();
        var refreshed = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.RefreshToken }, Ct);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        var rotated = (await refreshed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("refreshToken").GetString();

        // The app logs out with the token it held while the refresh was in flight.
        (await client.PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = user.RefreshToken }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated }, Ct)).StatusCode
            .Should().Be(HttpStatusCode.Unauthorized, "the replacement token of a logged-out session must not keep it alive");
    }

    // quadra-api:auth:login-rate-limit-shared-partition
    [Fact]
    public async Task Invalid_firebase_tokens_cannot_lock_other_people_out()
    {
        var firebase = Substitute.For<IFirebasePhoneTokenValidator>();
        firebase.ValidateAsync("bad", Arg.Any<CancellationToken>()).ThrowsAsync(new OidcTokenInvalidException("nope"));
        firebase.ValidateAsync("good", Arg.Any<CancellationToken>()).Returns("+5511977771234");
        using var host = _fx.Create("Testing", new Dictionary<string, string?>
            {
                ["Auth:PhoneVerification:Provider"] = "Firebase",
                ["Auth:PhoneVerification:Firebase:ProjectId"] = "quadra-volei-app",
                ["Auth:RateLimits:SmsOtpPerIpPerHour"] = "3",
            })
            .WithWebHostBuilder(b => b.ConfigureTestServices(s => s.AddSingleton(firebase)));
        var client = host.CreateClient(); // every TestServer request shares one partition, like users behind one proxy

        for (var i = 0; i < 5; i++)
        {
            await client.PostAsJsonAsync("/api/v1/auth/login/firebase-phone", new { idToken = "bad" }, Ct);
        }

        (await client.PostAsJsonAsync("/api/v1/auth/login/firebase-phone", new { idToken = "good" }, Ct)).StatusCode
            .Should().Be(HttpStatusCode.OK, "garbage tokens from the same address must not block a valid login");
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
