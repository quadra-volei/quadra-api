using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Quadra.IntegrationTests.Modules.Auth;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Sessions end for real (audit SEC-09, SEC-10): reusing a rotated refresh token revokes the
/// whole session, logout kills the access token too, and a token whose account or session does
/// not exist reads nothing.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class SessionAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public SessionAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task R10_reusing_a_rotated_refresh_token_revokes_the_session()
    {
        var user = await _fx.LoginAsync();
        var client = _fx.Factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.RefreshToken }, Ct);
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        var rotated = body.GetProperty("refreshToken").GetString();
        var newAccess = body.GetProperty("accessToken").GetString();

        var replay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = user.RefreshToken }, Ct);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized, "a used refresh token must not work twice");

        var afterReplay = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = rotated }, Ct);
        afterReplay.StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "after a replay the attacker's or the owner's newer token should be revoked too");

        var reader = _fx.Factory.CreateClient();
        reader.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newAccess);
        (await reader.GetAsync("/api/v1/matches/mine", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "the session's access tokens die with it");
    }

    [Fact]
    public async Task R10_access_token_stops_working_after_logout()
    {
        var user = await _fx.LoginAsync();
        (await _fx.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = user.RefreshToken }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await user.Client.GetAsync("/api/v1/matches/mine", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // Control: logging out one session leaves the account's other sessions alone, and a normal
    // refresh keeps the session working.
    [Fact]
    public async Task R10_control_other_sessions_and_normal_refresh_keep_working()
    {
        var phoneSession = await _fx.LoginAsync();
        var client = _fx.Factory.CreateClient();
        var second = await client.PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "verify", phoneNumber = phoneSession.Phone, code = TestJwtFactory.FakeOtpCode }, Ct);
        var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);

        var refreshed = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh", new { refreshToken = secondBody.GetProperty("refreshToken").GetString() }, Ct);
        refreshed.StatusCode.Should().Be(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", (await refreshed.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("accessToken").GetString());

        await _fx.Factory.CreateClient().PostAsJsonAsync("/api/v1/auth/logout", new { refreshToken = phoneSession.RefreshToken }, Ct);

        (await client.GetAsync("/api/v1/matches/mine", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_for_unknown_account_is_rejected_beyond_auth_me()
    {
        var client = _fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtFactory.CreateAccessToken(subject: Guid.NewGuid().ToString()));

        (await client.GetAsync("/api/v1/auth/me", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/v1/matches?page=1&pageSize=5", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized,
            "a token whose account does not exist (deleted, or signed with a leaked key) should not read data");
    }

    // A validly signed token for a real account but a session that was never issued.
    [Fact]
    public async Task Token_with_a_made_up_session_is_rejected()
    {
        var user = await _fx.LoginAsync();
        var client = _fx.Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestJwtFactory.CreateAccessToken(subject: user.Id.ToString(), sessionId: Guid.NewGuid()));

        (await client.GetAsync("/api/v1/matches/mine", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
