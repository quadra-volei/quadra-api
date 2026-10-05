using System.Net;
using System.Net.Http.Headers;
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
using Quadra.IntegrationTests.Modules.Auth;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Oidc;
using Quadra.Modules.Auth.Persistence;
using Quadra.Shared.Events.Auth;
using Testcontainers.PostgreSql;

namespace Quadra.IntegrationTests.Auth;

/// <summary>
/// HTTP integration tests for the FA.3 login endpoints. Spins up a real Postgres (Testcontainers),
/// uses the fake phone verification (fixed code) and substitutes the OIDC validator / event
/// publisher. Verifies FA.3 end-to-end: account creation on the first valid OTP or Google token,
/// the AuthTokensResponse shape, the issued access token being accepted by the API's own
/// middleware, hashed (never raw) refresh-token persistence, rotation, logout, the
/// exception→status mapping, and that UserLoggedIn fires on login but not on refresh.
/// </summary>
public sealed class LoginEndpointsTests : IClassFixture<LoginEndpointsTests.Fixture>
{
    private const string OtpEndpoint = "/api/v1/auth/login/sms-otp";
    private const string GoogleEndpoint = "/api/v1/auth/login/google";
    private const string RefreshEndpoint = "/api/v1/auth/refresh";
    private const string LogoutEndpoint = "/api/v1/auth/logout";
    private const string MeEndpoint = "/api/v1/auth/me";

    private readonly Fixture _fx;

    public LoginEndpointsTests(Fixture fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE refresh_tokens, users RESTART IDENTITY CASCADE;", Ct);

        _fx.Oidc.ClearSubstitute();
        _fx.Publisher.ClearSubstitute();
    }

    private async Task<T> QueryAsync<T>(Func<AuthDbContext, Task<T>> query)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        return await query(ctx);
    }

    private static Task<HttpResponseMessage> VerifyOtpAsync(
        HttpClient client,
        string phone,
        string code = TestJwtFactory.FakeOtpCode,
        string? deviceId = null) =>
        client.PostAsJsonAsync(
            OtpEndpoint,
            new { step = "verify", phoneNumber = phone, code, deviceId },
            Ct);

    private static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<JsonElement>(Ct);

    // =====================================================================
    // POST /login/sms-otp — initiate
    // =====================================================================

    /// <summary>
    /// Covers FA.3: "initiate sends a code to any phone number" — an unknown phone gets 200 (not
    /// the old 404), no user row is created yet, and the destination is masked.
    /// </summary>
    [Fact]
    public async Task Sms_otp_initiate_for_unknown_phone_returns_200_and_creates_no_user()
    {
        await ResetAsync();
        const string phone = "+5511955554444";

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(OtpEndpoint, new { step = "initiate", phoneNumber = phone }, Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(resp);
        json.GetProperty("delivery").GetProperty("deliveryMedium").GetString().Should().Be("SMS");
        json.GetProperty("delivery").GetProperty("deliveryDestination").GetString().Should().Be("+55*********44");

        (await QueryAsync(ctx => ctx.Users.CountAsync(Ct))).Should().Be(0);
    }

    /// <summary>
    /// Covers FA.3: "400 — validation failure (bad E.164)".
    /// </summary>
    [Fact]
    public async Task Sms_otp_with_bad_phone_returns_400()
    {
        await ResetAsync();

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(OtpEndpoint, new { step = "initiate", phoneNumber = "5511invalid" }, Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Covers FA.3: "400 — the code must be exactly 6 digits" (a 4-digit code is refused before
    /// reaching the SMS provider).
    /// </summary>
    [Fact]
    public async Task Sms_otp_verify_with_four_digit_code_returns_400()
    {
        await ResetAsync();

        var resp = await VerifyOtpAsync(_fx.Factory.CreateClient(), "+5511955550000", code: "1234");

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =====================================================================
    // POST /login/sms-otp — verify
    // =====================================================================

    /// <summary>
    /// Covers FA.3: "the first valid OTP creates the user" + "returns JWT + refresh token" +
    /// "refresh token persisted as SHA-256 hash (never raw)" + "UserRegistered and UserLoggedIn
    /// published", and FA.1: the issued access token is accepted by the API (GET /me).
    /// </summary>
    [Fact]
    public async Task Sms_otp_verify_for_new_phone_creates_user_and_issues_a_working_session()
    {
        await ResetAsync();
        const string phone = "+5511955551111";

        var client = _fx.Factory.CreateClient();
        var resp = await VerifyOtpAsync(client, phone, deviceId: "dev-otp");

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(resp);
        var userId = json.GetProperty("userId").GetGuid();
        var accessToken = json.GetProperty("accessToken").GetString()!;
        var refreshToken = json.GetProperty("refreshToken").GetString()!;
        json.GetProperty("tokenType").GetString().Should().Be("Bearer");
        json.GetProperty("expiresIn").GetInt32().Should().Be(15 * 60);
        json.GetProperty("isNewUser").GetBoolean().Should().BeTrue();

        var user = await QueryAsync(ctx => ctx.Users.SingleAsync(Ct));
        user.Id.Should().Be(userId);
        user.Provider.Should().Be(IdentityProvider.Phone);
        user.PhoneNumber.Should().Be(phone);

        var row = await QueryAsync(ctx => ctx.RefreshTokens.SingleAsync(Ct));
        row.TokenHash.Should().Be(RefreshTokenHasher.Hash(refreshToken));
        row.TokenHash.Should().NotBe(refreshToken);
        row.UserId.Should().Be(userId);
        row.DeviceId.Should().Be("dev-otp");
        row.RevokedAt.Should().BeNull();

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<UserRegistered>(e => e.UserId == userId && e.Provider == "phone" && e.PhoneNumber == phone),
            Arg.Any<CancellationToken>());
        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<UserLoggedIn>(e => e.UserId == userId && e.Provider == "phone" && e.DeviceId == "dev-otp"),
            Arg.Any<CancellationToken>());

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        var me = await client.GetAsync(MeEndpoint, Ct);
        me.StatusCode.Should().Be(HttpStatusCode.OK);
        var meJson = await ReadJsonAsync(me);
        meJson.GetProperty("userId").GetGuid().Should().Be(userId);
        meJson.GetProperty("provider").GetString().Should().Be("phone");
        meJson.GetProperty("phoneNumber").GetString().Should().Be(phone);
    }

    /// <summary>
    /// Covers FA.3: a second login of the same phone reuses the account (IsNewUser false, one
    /// users row, two sessions) and does not publish UserRegistered again.
    /// </summary>
    [Fact]
    public async Task Sms_otp_verify_for_returning_phone_reuses_the_user()
    {
        await ResetAsync();
        const string phone = "+5511955552222";
        var client = _fx.Factory.CreateClient();

        var first = await ReadJsonAsync(await VerifyOtpAsync(client, phone));
        var second = await ReadJsonAsync(await VerifyOtpAsync(client, phone));

        second.GetProperty("userId").GetGuid().Should().Be(first.GetProperty("userId").GetGuid());
        second.GetProperty("isNewUser").GetBoolean().Should().BeFalse();
        (await QueryAsync(ctx => ctx.Users.CountAsync(Ct))).Should().Be(1);
        (await QueryAsync(ctx => ctx.RefreshTokens.CountAsync(Ct))).Should().Be(2);
        await _fx.Publisher.Received(1).PublishAsync(Arg.Any<UserRegistered>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "401 — wrong code", and nothing is created.
    /// </summary>
    [Fact]
    public async Task Sms_otp_verify_with_wrong_code_returns_401_and_creates_nothing()
    {
        await ResetAsync();

        var resp = await VerifyOtpAsync(_fx.Factory.CreateClient(), "+5511955553333", code: "000000");

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await QueryAsync(ctx => ctx.Users.CountAsync(Ct))).Should().Be(0);
        (await QueryAsync(ctx => ctx.RefreshTokens.CountAsync(Ct))).Should().Be(0);
    }

    // =====================================================================
    // POST /login/google
    // =====================================================================

    /// <summary>
    /// Covers FA.3: "POST /login/google — validate the Google ID token, find or create the user,
    /// return JWT + refresh". The second login with the same Google account reuses the user.
    /// </summary>
    [Fact]
    public async Task Google_login_creates_the_user_once_and_reuses_it_afterwards()
    {
        await ResetAsync();
        _fx.Oidc.ValidateAsync("google.id.token", OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("google-sub-1", "Alice@Example.com", true));

        var client = _fx.Factory.CreateClient();
        var first = await client.PostAsJsonAsync(GoogleEndpoint, new { idToken = "google.id.token", deviceId = "dev-g" }, Ct);
        var second = await client.PostAsJsonAsync(GoogleEndpoint, new { idToken = "google.id.token" }, Ct);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstJson = await ReadJsonAsync(first);
        var secondJson = await ReadJsonAsync(second);
        firstJson.GetProperty("isNewUser").GetBoolean().Should().BeTrue();
        secondJson.GetProperty("isNewUser").GetBoolean().Should().BeFalse();
        secondJson.GetProperty("userId").GetGuid().Should().Be(firstJson.GetProperty("userId").GetGuid());

        var user = await QueryAsync(ctx => ctx.Users.SingleAsync(Ct));
        user.Provider.Should().Be(IdentityProvider.Google);
        user.ExternalSubject.Should().Be("google-sub-1");
        user.Email.Should().Be("alice@example.com");

        await _fx.Publisher.Received(2).PublishAsync(
            Arg.Is<UserLoggedIn>(e => e.UserId == user.Id && e.Provider == "google"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers FA.3: "401 — invalid Google ID token".
    /// </summary>
    [Fact]
    public async Task Google_login_with_invalid_token_returns_401()
    {
        await ResetAsync();
        _fx.Oidc.ValidateAsync(Arg.Any<string>(), OidcProvider.Google, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OidcTokenInvalidException("bad"));

        var resp = await _fx.Factory.CreateClient()
            .PostAsJsonAsync(GoogleEndpoint, new { idToken = "bad.token" }, Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await QueryAsync(ctx => ctx.Users.CountAsync(Ct))).Should().Be(0);
    }

    /// <summary>
    /// Covers FA.3: "400 — empty ID token".
    /// </summary>
    [Fact]
    public async Task Google_login_with_empty_token_returns_400()
    {
        await ResetAsync();

        var resp = await _fx.Factory.CreateClient()
            .PostAsJsonAsync(GoogleEndpoint, new { idToken = "" }, Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =====================================================================
    // POST /refresh
    // =====================================================================

    /// <summary>
    /// Covers FA.3: "refresh rotates the refresh token" — a new pair is returned, the old row is
    /// revoked, the old raw token no longer works, the new one does, and no UserLoggedIn is
    /// published for the refresh.
    /// </summary>
    [Fact]
    public async Task Refresh_rotates_the_token_and_the_old_one_stops_working()
    {
        await ResetAsync();
        var client = _fx.Factory.CreateClient();
        var login = await ReadJsonAsync(await VerifyOtpAsync(client, "+5511955556666", deviceId: "dev-r"));
        var oldRefresh = login.GetProperty("refreshToken").GetString()!;
        _fx.Publisher.ClearReceivedCalls();

        var resp = await client.PostAsJsonAsync(RefreshEndpoint, new { refreshToken = oldRefresh }, Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await ReadJsonAsync(resp);
        var newRefresh = json.GetProperty("refreshToken").GetString()!;
        newRefresh.Should().NotBe(oldRefresh);
        json.GetProperty("accessToken").GetString().Should().NotBeNullOrWhiteSpace();
        json.GetProperty("userId").GetGuid().Should().Be(login.GetProperty("userId").GetGuid());

        var rows = await QueryAsync(ctx => ctx.RefreshTokens.ToListAsync(Ct));
        rows.Should().HaveCount(2);
        rows.Single(r => r.TokenHash == RefreshTokenHasher.Hash(oldRefresh)).RevokedAt.Should().NotBeNull();
        var active = rows.Single(r => r.TokenHash == RefreshTokenHasher.Hash(newRefresh));
        active.RevokedAt.Should().BeNull();
        active.DeviceId.Should().Be("dev-r");

        await _fx.Publisher.DidNotReceive().PublishAsync(Arg.Any<UserLoggedIn>(), Arg.Any<CancellationToken>());

        var replay = await client.PostAsJsonAsync(RefreshEndpoint, new { refreshToken = oldRefresh }, Ct);
        replay.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var again = await client.PostAsJsonAsync(RefreshEndpoint, new { refreshToken = newRefresh }, Ct);
        again.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Covers the concurrent-refresh guard against the real database: two simultaneous refreshes
    /// of one token yield exactly one success.
    /// </summary>
    [Fact]
    public async Task Concurrent_refreshes_of_the_same_token_succeed_only_once()
    {
        await ResetAsync();
        var client = _fx.Factory.CreateClient();
        var login = await ReadJsonAsync(await VerifyOtpAsync(client, "+5511955557777"));
        var refreshToken = login.GetProperty("refreshToken").GetString()!;

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            client.PostAsJsonAsync(RefreshEndpoint, new { refreshToken }, Ct)));

        responses.Count(r => r.StatusCode == HttpStatusCode.OK).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Unauthorized).Should().Be(3);
        (await QueryAsync(ctx => ctx.RefreshTokens.CountAsync(t => t.RevokedAt == null, Ct))).Should().Be(1);
    }

    /// <summary>
    /// Covers FA.3: "401 — refresh token unknown".
    /// </summary>
    [Fact]
    public async Task Refresh_with_unknown_token_returns_401()
    {
        await ResetAsync();

        var resp = await _fx.Factory.CreateClient()
            .PostAsJsonAsync(RefreshEndpoint, new { refreshToken = "never-issued" }, Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.3: "401 — refresh token expired".
    /// </summary>
    [Fact]
    public async Task Refresh_with_expired_token_returns_401()
    {
        await ResetAsync();
        var client = _fx.Factory.CreateClient();
        var login = await ReadJsonAsync(await VerifyOtpAsync(client, "+5511955558888"));
        var refreshToken = login.GetProperty("refreshToken").GetString()!;
        await QueryAsync(ctx => ctx.Database.ExecuteSqlRawAsync(
            "UPDATE refresh_tokens SET expires_at = now() - interval '1 minute';", Ct));

        var resp = await client.PostAsJsonAsync(RefreshEndpoint, new { refreshToken }, Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers FA.3: "400 — empty refresh token".
    /// </summary>
    [Fact]
    public async Task Refresh_with_empty_token_returns_400()
    {
        await ResetAsync();

        var resp = await _fx.Factory.CreateClient()
            .PostAsJsonAsync(RefreshEndpoint, new { refreshToken = "" }, Ct);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // =====================================================================
    // POST /logout and GET /me
    // =====================================================================

    /// <summary>
    /// Covers logout: 204, the refresh token is revoked and can no longer be refreshed; a second
    /// logout with the same token is still 204.
    /// </summary>
    [Fact]
    public async Task Logout_revokes_the_refresh_token()
    {
        await ResetAsync();
        var client = _fx.Factory.CreateClient();
        var login = await ReadJsonAsync(await VerifyOtpAsync(client, "+5511955559999"));
        var refreshToken = login.GetProperty("refreshToken").GetString()!;

        var logout = await client.PostAsJsonAsync(LogoutEndpoint, new { refreshToken }, Ct);
        var logoutAgain = await client.PostAsJsonAsync(LogoutEndpoint, new { refreshToken }, Ct);
        var refresh = await client.PostAsJsonAsync(RefreshEndpoint, new { refreshToken }, Ct);

        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        logoutAgain.StatusCode.Should().Be(HttpStatusCode.NoContent);
        refresh.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Covers GET /me: requires a token, and a valid token for a user that does not exist is 401.
    /// </summary>
    [Fact]
    public async Task Me_requires_a_token_for_an_existing_user()
    {
        await ResetAsync();
        var client = _fx.Factory.CreateClient();

        var anonymous = await client.GetAsync(MeEndpoint, Ct);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            TestJwtFactory.CreateAccessToken(subject: Guid.NewGuid().ToString()));
        var ghost = await client.GetAsync(MeEndpoint, Ct);

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        ghost.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =====================================================================
    // Fixture: spins up Postgres + WebApplicationFactory once for the class
    // =====================================================================

    public sealed class Fixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
            .Build();

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
                        config.AddInMemoryCollection(TestJwtFactory.AuthSettings());
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Auth"] = _postgres.GetConnectionString(),
                            ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                            // Matches module fail-fast: SQS queue URLs required at startup.
                            ["Aws:Sqs:MatchCreatedQueueUrl"] = "http://test-stub/match-created",
                            ["Aws:Sqs:MatchStatusChangedQueueUrl"] = "http://test-stub/match-status-changed",
                        });
                    });

                    builder.ConfigureTestServices(services =>
                    {
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
