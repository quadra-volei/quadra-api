using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using NSubstitute.ClearExtensions;
using NSubstitute.ExceptionExtensions;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Cognito;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Oidc;
using Quadra.Modules.Auth.Persistence;
using Quadra.Shared.Events.Auth;
using Testcontainers.PostgreSql;

namespace Quadra.IntegrationTests.Auth;

/// <summary>
/// HTTP integration tests for the FA.3 login endpoints. Spins up a real Postgres (Testcontainers)
/// and substitutes the Cognito auth client / OIDC validator / SQS publisher. Verifies every FA.3
/// acceptance criterion end-to-end: the four endpoints, the AuthTokensResponse shape, the hashed
/// (never raw) refresh-token persistence, the confirmation flip, rotation revoking the old row,
/// the exception→status mapping, and that UserLoggedIn fires on login but not on refresh.
/// </summary>
public sealed class LoginEndpointsTests : IClassFixture<LoginEndpointsTests.Fixture>
{
    private readonly Fixture _fx;

    public LoginEndpointsTests(Fixture fx)
    {
        _fx = fx;
    }

    private async Task ResetDbAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE refresh_tokens, users RESTART IDENTITY CASCADE;");
    }

    private void ResetMocks()
    {
        _fx.CognitoAuth.ClearSubstitute();
        _fx.Oidc.ClearSubstitute();
        _fx.Publisher.ClearSubstitute();
    }

    private async Task<User> SeedPhoneUserAsync(string phone, string cognitoSub, UserConfirmationStatus status)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var user = User.CreateForPhone(Guid.NewGuid(), cognitoSub, phone, DateTimeOffset.UtcNow);
        if (status == UserConfirmationStatus.Confirmed)
        {
            user.Confirm(DateTimeOffset.UtcNow);
        }

        ctx.Users.Add(user);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    private async Task<User> SeedExternalUserAsync(IdentityProvider provider, string cognitoSub)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var user = User.CreateForExternal(Guid.NewGuid(), cognitoSub, provider, "u@example.com", DateTimeOffset.UtcNow);
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return user;
    }

    // =====================================================================
    // POST /login/sms-otp — initiate
    // =====================================================================

    /// <summary>
    /// Covers FA.3: "404 — no local users row exists for the phone number" on initiate.
    /// </summary>
    [Fact]
    public async Task Sms_otp_initiate_without_local_user_returns_404()
    {
        await ResetDbAsync();
        ResetMocks();

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "initiate", phoneNumber = "+5511955554444" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers FA.3: "POST /login/sms-otp (initiate)" — returns 200 with the opaque session + masked
    /// delivery details.
    /// </summary>
    [Fact]
    public async Task Sms_otp_initiate_with_local_user_returns_200_and_session()
    {
        await ResetDbAsync();
        ResetMocks();
        const string phone = "+5511955553333";
        await SeedPhoneUserAsync(phone, "cog-sub-otp", UserConfirmationStatus.Unconfirmed);

        _fx.CognitoAuth.InitiateSmsOtpAsync(phone, Arg.Any<CancellationToken>())
            .Returns(new SmsOtpChallenge("opaque-session", "SMS", "+55********33"));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "initiate", phoneNumber = phone },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("session").GetString().Should().Be("opaque-session");
        json.GetProperty("delivery").GetProperty("deliveryMedium").GetString().Should().Be("SMS");
        json.GetProperty("delivery").GetProperty("deliveryDestination").GetString().Should().Be("+55********33");
    }

    /// <summary>
    /// Covers FA.3: "429 — Cognito throttling (TooManyRequests / LimitExceeded)" maps to 429.
    /// </summary>
    [Fact]
    public async Task Sms_otp_initiate_when_throttled_returns_429()
    {
        await ResetDbAsync();
        ResetMocks();
        const string phone = "+5511955552222";
        await SeedPhoneUserAsync(phone, "cog-sub-throttle", UserConfirmationStatus.Unconfirmed);

        _fx.CognitoAuth.InitiateSmsOtpAsync(phone, Arg.Any<CancellationToken>())
            .ThrowsAsync(new CognitoAuthThrottledException("slow down"));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "initiate", phoneNumber = phone },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be((HttpStatusCode)429);
    }

    /// <summary>
    /// Covers FA.3: "400 — validation failure (bad E.164)".
    /// </summary>
    [Fact]
    public async Task Sms_otp_with_bad_phone_returns_400()
    {
        await ResetDbAsync();
        ResetMocks();

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "initiate", phoneNumber = "5511invalid" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =====================================================================
    // POST /login/sms-otp — verify
    // =====================================================================

    /// <summary>
    /// Covers FA.3: "POST /login/sms-otp (verify)" + "confirmation_status flip on first successful
    /// verify" + "returns JWT + refresh token (AuthTokensResponse shape)" + "refresh token persisted
    /// as SHA-256 hash (never raw)" + "UserLoggedIn event published on login".
    /// </summary>
    [Fact]
    public async Task Sms_otp_verify_flips_confirmation_persists_hashed_token_and_publishes_event()
    {
        await ResetDbAsync();
        ResetMocks();
        const string phone = "+5511955551111";
        const string cognitoSub = "cog-sub-verify";
        const string rawRefresh = "raw-refresh-otp";
        var seeded = await SeedPhoneUserAsync(phone, cognitoSub, UserConfirmationStatus.Unconfirmed);

        _fx.CognitoAuth.RespondToSmsOtpAsync(phone, "opaque-session", "123456", Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("access-jwt", "id-jwt", rawRefresh, 3600,
                DateTimeOffset.UtcNow.AddDays(30), cognitoSub));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "verify", phoneNumber = phone, code = "123456", session = "opaque-session", deviceId = "dev-otp" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("accessToken").GetString().Should().Be("access-jwt");
        json.GetProperty("idToken").GetString().Should().Be("id-jwt");
        json.GetProperty("refreshToken").GetString().Should().Be(rawRefresh);
        json.GetProperty("tokenType").GetString().Should().Be("Bearer");
        json.GetProperty("expiresIn").GetInt32().Should().Be(3600);
        json.GetProperty("userId").GetGuid().Should().Be(seeded.Id);
        json.GetProperty("cognitoSub").GetString().Should().Be(cognitoSub);
        json.GetProperty("confirmationStatus").GetString().Should().Be("Confirmed");

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            // Confirmation flip persisted.
            var user = await ctx.Users.SingleAsync(u => u.PhoneNumber == phone, TestContext.Current.CancellationToken);
            user.ConfirmationStatus.Should().Be(UserConfirmationStatus.Confirmed);

            // Refresh token persisted as SHA-256 hash, never the raw token.
            var stored = await ctx.RefreshTokens.SingleAsync(t => t.UserId == seeded.Id, TestContext.Current.CancellationToken);
            stored.TokenHash.Should().Be(RefreshTokenHasher.Hash(rawRefresh));
            stored.TokenHash.Should().NotBe(rawRefresh);
            stored.CognitoSub.Should().Be(cognitoSub);
            stored.DeviceId.Should().Be("dev-otp");
            stored.RevokedAt.Should().BeNull();
        }

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<UserLoggedIn>(e => e.UserId == seeded.Id && e.Provider == "phone" && e.DeviceId == "dev-otp"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "401 — wrong/expired OTP code" maps to 401; no token persisted.
    /// </summary>
    [Fact]
    public async Task Sms_otp_verify_with_wrong_code_returns_401()
    {
        await ResetDbAsync();
        ResetMocks();
        const string phone = "+5511955550000";
        await SeedPhoneUserAsync(phone, "cog-sub-wrong", UserConfirmationStatus.Unconfirmed);

        _fx.CognitoAuth.RespondToSmsOtpAsync(phone, Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOtpException("wrong code"));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "verify", phoneNumber = phone, code = "999999", session = "s" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _fx.Publisher.DidNotReceiveWithAnyArgs()
            .PublishAsync<UserLoggedIn>(Arg.Any<UserLoggedIn>(), Arg.Any<CancellationToken>());
    }

    // =====================================================================
    // POST /login/google + /login/apple
    // =====================================================================

    /// <summary>
    /// Covers FA.3: "POST /login/google (validate token → broker Cognito session → tokens)" +
    /// AuthTokensResponse shape + hashed refresh-token persistence + UserLoggedIn on login.
    /// </summary>
    [Fact]
    public async Task Google_login_returns_tokens_persists_hashed_token_and_publishes_event()
    {
        await ResetDbAsync();
        ResetMocks();
        const string cognitoSub = "cog-sub-google-login";
        const string rawRefresh = "raw-refresh-google";
        var seeded = await SeedExternalUserAsync(IdentityProvider.Google, cognitoSub);

        _fx.Oidc.ValidateAsync("good.google.token", OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("google-ext-sub", "u@example.com", true));
        _fx.CognitoAuth.BrokerExternalSessionAsync(IdentityProvider.Google, "google-ext-sub", Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("g-access", "g-id", rawRefresh, 3600,
                DateTimeOffset.UtcNow.AddDays(30), cognitoSub));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/google",
            new { idToken = "good.google.token", deviceId = "dev-g" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("accessToken").GetString().Should().Be("g-access");
        json.GetProperty("refreshToken").GetString().Should().Be(rawRefresh);
        json.GetProperty("tokenType").GetString().Should().Be("Bearer");
        json.GetProperty("userId").GetGuid().Should().Be(seeded.Id);

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var stored = await ctx.RefreshTokens.SingleAsync(t => t.UserId == seeded.Id, TestContext.Current.CancellationToken);
            stored.TokenHash.Should().Be(RefreshTokenHasher.Hash(rawRefresh));
            stored.TokenHash.Should().NotBe(rawRefresh);
        }

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<UserLoggedIn>(e => e.UserId == seeded.Id && e.Provider == "google"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "POST /login/apple (idem, Apple provider)" — token validated with Apple,
    /// event Provider == "apple".
    /// </summary>
    [Fact]
    public async Task Apple_login_returns_tokens_and_publishes_apple_event()
    {
        await ResetDbAsync();
        ResetMocks();
        const string cognitoSub = "cog-sub-apple-login";
        var seeded = await SeedExternalUserAsync(IdentityProvider.Apple, cognitoSub);

        _fx.Oidc.ValidateAsync("good.apple.token", OidcProvider.Apple, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("apple-ext-sub", "relay@privaterelay.appleid.com", true));
        _fx.CognitoAuth.BrokerExternalSessionAsync(IdentityProvider.Apple, "apple-ext-sub", Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("a-access", "a-id", "raw-refresh-apple", 3600,
                DateTimeOffset.UtcNow.AddDays(30), cognitoSub));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/apple",
            new { idToken = "good.apple.token" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        await _fx.Oidc.Received(1).ValidateAsync("good.apple.token", OidcProvider.Apple, Arg.Any<CancellationToken>());
        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<UserLoggedIn>(e => e.UserId == seeded.Id && e.Provider == "apple"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "401 — Google ID token fails validation".
    /// </summary>
    [Fact]
    public async Task Google_login_with_invalid_token_returns_401()
    {
        await ResetDbAsync();
        ResetMocks();

        _fx.Oidc.ValidateAsync(Arg.Any<string>(), OidcProvider.Google, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OidcTokenInvalidException("bad signature"));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/google",
            new { idToken = "bad.token" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.3: "404 — token valid but no linked Cognito / local user" (broker throws).
    /// </summary>
    [Fact]
    public async Task Google_login_with_unlinked_user_returns_404()
    {
        await ResetDbAsync();
        ResetMocks();

        _fx.Oidc.ValidateAsync(Arg.Any<string>(), OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("unlinked-sub", "u@example.com", true));
        _fx.CognitoAuth.BrokerExternalSessionAsync(IdentityProvider.Google, "unlinked-sub", Arg.Any<CancellationToken>())
            .ThrowsAsync(new UserNotFoundForLoginException("no linked user"));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/google",
            new { idToken = "tok" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers FA.3: "502 — unexpected Cognito service error".
    /// </summary>
    [Fact]
    public async Task Google_login_when_cognito_unavailable_returns_502()
    {
        await ResetDbAsync();
        ResetMocks();

        _fx.Oidc.ValidateAsync(Arg.Any<string>(), OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("ext-sub", "u@example.com", true));
        _fx.CognitoAuth.BrokerExternalSessionAsync(IdentityProvider.Google, "ext-sub", Arg.Any<CancellationToken>())
            .ThrowsAsync(new CognitoUnavailableException("boom"));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/google",
            new { idToken = "tok" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    /// <summary>
    /// Covers FA.3: "400 — validation failure (empty token)".
    /// </summary>
    [Fact]
    public async Task Google_login_with_empty_token_returns_400()
    {
        await ResetDbAsync();
        ResetMocks();

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/login/google",
            new { idToken = "" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =====================================================================
    // POST /refresh
    // =====================================================================

    /// <summary>
    /// Covers FA.3: "POST /refresh — unknown refresh token → 401".
    /// </summary>
    [Fact]
    public async Task Refresh_with_unknown_token_returns_401()
    {
        await ResetDbAsync();
        ResetMocks();

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = "unknown-token" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.3: "POST /refresh — revoked token → 401".
    /// </summary>
    [Fact]
    public async Task Refresh_with_revoked_token_returns_401()
    {
        await ResetDbAsync();
        ResetMocks();
        const string raw = "revoked-raw-token";
        var user = await SeedExternalUserAsync(IdentityProvider.Google, "cog-sub-revoked");

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var token = RefreshToken.Issue(Guid.NewGuid(), user.Id, RefreshTokenHasher.Hash(raw),
                user.CognitoSub, null, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29));
            token.Revoke(DateTimeOffset.UtcNow);
            ctx.RefreshTokens.Add(token);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = raw },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _fx.CognitoAuth.DidNotReceiveWithAnyArgs()
            .RefreshAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "POST /refresh — expired token → 401".
    /// </summary>
    [Fact]
    public async Task Refresh_with_expired_token_returns_401()
    {
        await ResetDbAsync();
        ResetMocks();
        const string raw = "expired-raw-token";
        var user = await SeedExternalUserAsync(IdentityProvider.Google, "cog-sub-expired");

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var token = RefreshToken.Issue(Guid.NewGuid(), user.Id, RefreshTokenHasher.Hash(raw),
                user.CognitoSub, null, DateTimeOffset.UtcNow.AddDays(-40), DateTimeOffset.UtcNow.AddDays(-10));
            ctx.RefreshTokens.Add(token);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = raw },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.3: "POST /refresh — rotation revoking the old row" + new hashed row persisted +
    /// "no UserLoggedIn event on refresh".
    /// </summary>
    [Fact]
    public async Task Refresh_with_rotation_revokes_old_row_persists_new_and_does_not_publish()
    {
        await ResetDbAsync();
        ResetMocks();
        const string oldRaw = "old-raw-token";
        const string newRaw = "rotated-raw-token";
        const string cognitoSub = "cog-sub-rotate";
        var user = await SeedExternalUserAsync(IdentityProvider.Google, cognitoSub);

        Guid oldRowId;
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var token = RefreshToken.Issue(Guid.NewGuid(), user.Id, RefreshTokenHasher.Hash(oldRaw),
                cognitoSub, null, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29));
            ctx.RefreshTokens.Add(token);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
            oldRowId = token.Id;
        }

        _fx.CognitoAuth.RefreshAsync(oldRaw, cognitoSub, Arg.Any<CancellationToken>())
            .Returns(new CognitoAuthResult("new-access", "new-id", newRaw, 3600,
                DateTimeOffset.UtcNow.AddDays(30), cognitoSub));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = oldRaw, deviceId = "dev-rot" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("accessToken").GetString().Should().Be("new-access");
        json.GetProperty("refreshToken").GetString().Should().Be(newRaw);

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();

            // Old row revoked.
            var oldRow = await ctx.RefreshTokens.SingleAsync(t => t.Id == oldRowId, TestContext.Current.CancellationToken);
            oldRow.RevokedAt.Should().NotBeNull();

            // New row persisted, hashed (never raw), active.
            var newRow = await ctx.RefreshTokens.SingleAsync(t => t.Id != oldRowId, TestContext.Current.CancellationToken);
            newRow.TokenHash.Should().Be(RefreshTokenHasher.Hash(newRaw));
            newRow.TokenHash.Should().NotBe(newRaw);
            newRow.DeviceId.Should().Be("dev-rot");
            newRow.RevokedAt.Should().BeNull();
        }

        // No UserLoggedIn on refresh.
        await _fx.Publisher.DidNotReceiveWithAnyArgs()
            .PublishAsync<UserLoggedIn>(Arg.Any<UserLoggedIn>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "POST /refresh — Cognito rejects → 401 and the local row is revoked".
    /// </summary>
    [Fact]
    public async Task Refresh_when_cognito_rejects_returns_401_and_revokes_row()
    {
        await ResetDbAsync();
        ResetMocks();
        const string raw = "cognito-rejected-token";
        const string cognitoSub = "cog-sub-reject";
        var user = await SeedExternalUserAsync(IdentityProvider.Google, cognitoSub);

        Guid rowId;
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            var token = RefreshToken.Issue(Guid.NewGuid(), user.Id, RefreshTokenHasher.Hash(raw),
                cognitoSub, null, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(29));
            ctx.RefreshTokens.Add(token);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
            rowId = token.Id;
        }

        _fx.CognitoAuth.RefreshAsync(raw, cognitoSub, Arg.Any<CancellationToken>())
            .ThrowsAsync(new RefreshTokenRejectedException("Cognito NotAuthorized"));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = raw },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var verifyScope = _fx.Factory.Services.CreateScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var row = await verifyCtx.RefreshTokens.SingleAsync(t => t.Id == rowId, TestContext.Current.CancellationToken);
        row.RevokedAt.Should().NotBeNull();
    }

    /// <summary>
    /// Covers FA.3: "400 — validation failure (empty token)" on refresh.
    /// </summary>
    [Fact]
    public async Task Refresh_with_empty_token_returns_400()
    {
        await ResetDbAsync();
        ResetMocks();

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/refresh",
            new { refreshToken = "" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =====================================================================
    // Fixture: spins up Postgres + WebApplicationFactory once for the class
    // =====================================================================

    public sealed class Fixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
            .Build();

        public ICognitoAuthClient CognitoAuth { get; private set; } = Substitute.For<ICognitoAuthClient>();
        public IOidcTokenValidator Oidc { get; private set; } = Substitute.For<IOidcTokenValidator>();
        public IEventPublisher Publisher { get; private set; } = Substitute.For<IEventPublisher>();

        public WebApplicationFactory<Program> Factory { get; private set; } = default!;

        public async ValueTask InitializeAsync()
        {
            await _postgres.StartAsync();

            Factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Testing");

                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["Auth:Cognito:UserPoolId"] = "us-east-1_TESTPOOL",
                            ["Auth:Cognito:Region"] = "us-east-1",
                            ["Auth:Cognito:Audience"] = "test-aud",
                            ["Auth:Cognito:AppClientId"] = "test-app-client",
                            ["Auth:Cognito:ClockSkewSeconds"] = "30",
                            ["Auth:Google:ClientId"] = "google-client",
                            ["Auth:Google:Issuer"] = "https://accounts.google.com",
                            ["Auth:Google:JwksUri"] = "https://accounts.google.com/.well-known/openid-configuration",
                            ["Auth:Apple:ClientId"] = "apple-client",
                            ["Auth:Apple:Issuer"] = "https://appleid.apple.com",
                            ["Auth:Apple:JwksUri"] = "https://appleid.apple.com/.well-known/openid-configuration",
                            ["ConnectionStrings:Auth"] = _postgres.GetConnectionString(),
                            ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                        });
                    });

                    builder.ConfigureTestServices(services =>
                    {
                        ReplaceService(services, typeof(ICognitoAuthClient), CognitoAuth);
                        ReplaceService(services, typeof(IOidcTokenValidator), Oidc);
                        ReplaceService(services, typeof(IEventPublisher), Publisher);
                    });
                });

            using var scope = Factory.Services.CreateScope();
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            await ctx.Database.MigrateAsync();
        }

        public async ValueTask DisposeAsync()
        {
            Factory.Dispose();
            await _postgres.DisposeAsync();
        }

        private static void ReplaceService(IServiceCollection services, Type serviceType, object instance)
        {
            var descriptors = services.Where(d => d.ServiceType == serviceType).ToList();
            foreach (var d in descriptors)
            {
                services.Remove(d);
            }

            services.AddSingleton(serviceType, instance);
        }
    }
}
