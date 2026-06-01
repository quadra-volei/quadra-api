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
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Oidc;
using Quadra.Modules.Auth.Persistence;
using Quadra.Shared.Events.Auth;
using Testcontainers.PostgreSql;

namespace Quadra.IntegrationTests.Auth;

/// <summary>
/// HTTP integration tests for POST /api/v1/auth/signup. Spins up a real Postgres in Docker
/// (Testcontainers), substitutes the Cognito SDK / OIDC validator / SQS publisher, and verifies
/// every acceptance criterion of FA.2 end-to-end.
/// </summary>
public sealed class SignupEndpointTests : IClassFixture<SignupEndpointTests.Fixture>
{
    private readonly Fixture _fx;

    public SignupEndpointTests(Fixture fx)
    {
        _fx = fx;
    }

    private async Task ResetDbAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE users RESTART IDENTITY CASCADE;");
    }

    private void ResetMocks()
    {
        _fx.Cognito.ClearSubstitute();
        _fx.Oidc.ClearSubstitute();
        _fx.Publisher.ClearSubstitute();
    }

    // ============= success: phone =============

    /// <summary>
    /// Covers acceptance criteria:
    /// - "POST /api/v1/auth/signup exists and triggers Cognito SignUp (phone)"
    /// - "Local user row is persisted (id, cognito_sub, provider, phone_number, confirmation_status, created_at)"
    /// - "Response returns user id and confirmation status"
    /// - "Phone-signup confirmation_status starts Unconfirmed"
    /// - "UserRegistered event is published after DB commit".
    /// </summary>
    [Fact]
    public async Task Post_phone_signup_returns_201_persists_user_and_publishes_event()
    {
        await ResetDbAsync();
        ResetMocks();
        const string phone = "+5511988887777";

        _fx.Cognito.SignUpPhoneAsync(phone, Arg.Any<CancellationToken>())
            .Returns(new PhoneSignupResult("cog-sub-phone-1", "SMS", "+55********77"));

        var client = _fx.Factory.CreateClient();
        var body = new
        {
            provider = "phone",
            phone = new { phoneNumber = phone },
        };

        var resp = await client.PostAsJsonAsync("/api/v1/auth/signup", body, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("userId").GetGuid().Should().NotBeEmpty();
        json.GetProperty("provider").GetString().Should().Be("phone");
        json.GetProperty("confirmationStatus").GetString().Should().Be("Unconfirmed");
        json.GetProperty("cognitoSub").GetString().Should().Be("cog-sub-phone-1");
        json.GetProperty("smsDelivery").GetProperty("deliveryMedium").GetString().Should().Be("SMS");

        // Persistence check
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var persisted = await ctx.Users.SingleAsync(u => u.PhoneNumber == phone, TestContext.Current.CancellationToken);
        persisted.Provider.Should().Be(IdentityProvider.Phone);
        persisted.CognitoSub.Should().Be("cog-sub-phone-1");
        persisted.ConfirmationStatus.Should().Be(UserConfirmationStatus.Unconfirmed);
        persisted.Email.Should().BeNull();
        persisted.CreatedAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(-1));

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<UserRegistered>(e =>
                e.Provider == "phone"
                && e.PhoneNumber == phone
                && e.ConfirmationStatus == "Unconfirmed"),
            Arg.Any<CancellationToken>());
    }

    // ============= success: google =============

    /// <summary>
    /// Covers: "POST /signup triggers AdminCreateUser/AdminLinkProviderForUser (Google/Apple)"
    /// + "Google/Apple start Confirmed"
    /// + persistence and event-after-commit.
    /// </summary>
    [Fact]
    public async Task Post_google_signup_returns_201_persists_confirmed_user_and_publishes_event()
    {
        await ResetDbAsync();
        ResetMocks();

        _fx.Oidc.ValidateAsync("good.google.token", OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("google-sub-1", "alice@example.com", true));
        _fx.Cognito.FindExternalUserSubAsync(IdentityProvider.Google, "google-sub-1", Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _fx.Cognito.ProvisionExternalUserAsync(
            IdentityProvider.Google, "google-sub-1", "alice@example.com", true, Arg.Any<CancellationToken>())
            .Returns(new ExternalSignupResult("cog-sub-google-1"));

        var client = _fx.Factory.CreateClient();
        var body = new
        {
            provider = "google",
            google = new { idToken = "good.google.token" },
        };

        var resp = await client.PostAsJsonAsync("/api/v1/auth/signup", body, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("provider").GetString().Should().Be("google");
        json.GetProperty("confirmationStatus").GetString().Should().Be("Confirmed");
        json.GetProperty("cognitoSub").GetString().Should().Be("cog-sub-google-1");

        await _fx.Cognito.Received(1).LinkExternalIdentityAsync(
            IdentityProvider.Google, "cog-sub-google-1", "google-sub-1", Arg.Any<CancellationToken>());

        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        var persisted = await ctx.Users.SingleAsync(u => u.CognitoSub == "cog-sub-google-1", TestContext.Current.CancellationToken);
        persisted.Provider.Should().Be(IdentityProvider.Google);
        persisted.Email.Should().Be("alice@example.com");
        persisted.PhoneNumber.Should().BeNull();
        persisted.ConfirmationStatus.Should().Be(UserConfirmationStatus.Confirmed);

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<UserRegistered>(e =>
                e.Provider == "google" && e.ConfirmationStatus == "Confirmed"),
            Arg.Any<CancellationToken>());
    }

    // ============= success: apple =============

    /// <summary>
    /// Covers: Apple branch, AdminCreateUser/AdminLink path, Confirmed status.
    /// </summary>
    [Fact]
    public async Task Post_apple_signup_returns_201_with_confirmed_status()
    {
        await ResetDbAsync();
        ResetMocks();

        _fx.Oidc.ValidateAsync("good.apple.token", OidcProvider.Apple, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("apple-sub-1", "relay@privaterelay.appleid.com", true));
        _fx.Cognito.FindExternalUserSubAsync(IdentityProvider.Apple, "apple-sub-1", Arg.Any<CancellationToken>())
            .Returns((string?)null);
        _fx.Cognito.ProvisionExternalUserAsync(
            IdentityProvider.Apple, "apple-sub-1", "relay@privaterelay.appleid.com", true, Arg.Any<CancellationToken>())
            .Returns(new ExternalSignupResult("cog-sub-apple-1"));

        var client = _fx.Factory.CreateClient();
        var body = new
        {
            provider = "apple",
            apple = new { idToken = "good.apple.token" },
        };

        var resp = await client.PostAsJsonAsync("/api/v1/auth/signup", body, TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        json.GetProperty("provider").GetString().Should().Be("apple");
        json.GetProperty("confirmationStatus").GetString().Should().Be("Confirmed");
    }

    // ============= error paths =============

    /// <summary>
    /// Covers: "400 — request fails validation (malformed E.164 phone)".
    /// </summary>
    [Fact]
    public async Task Post_phone_signup_with_invalid_E164_returns_400()
    {
        await ResetDbAsync();
        ResetMocks();
        var client = _fx.Factory.CreateClient();

        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/signup",
            new { provider = "phone", phone = new { phoneNumber = "5511invalid" } },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await _fx.Cognito.DidNotReceiveWithAnyArgs().SignUpPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: "400 — request fails validation (missing payload for declared provider)".
    /// </summary>
    [Fact]
    public async Task Post_phone_signup_without_payload_returns_400()
    {
        await ResetDbAsync();
        ResetMocks();
        var client = _fx.Factory.CreateClient();

        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/signup",
            new { provider = "phone" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Covers: "400 — unknown provider".
    /// </summary>
    [Fact]
    public async Task Post_signup_with_unknown_provider_returns_400()
    {
        await ResetDbAsync();
        ResetMocks();
        var client = _fx.Factory.CreateClient();

        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/signup",
            new { provider = "facebook" },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Covers: "401 — supplied Google/Apple ID token fails validation".
    /// </summary>
    [Fact]
    public async Task Post_google_signup_with_invalid_token_returns_401()
    {
        await ResetDbAsync();
        ResetMocks();
        _fx.Oidc.ValidateAsync(Arg.Any<string>(), OidcProvider.Google, Arg.Any<CancellationToken>())
            .ThrowsAsync(new OidcTokenInvalidException("bad signature"));

        var client = _fx.Factory.CreateClient();

        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/signup",
            new { provider = "google", google = new { idToken = "bad.token" } },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await _fx.Publisher.DidNotReceiveWithAnyArgs().PublishAsync<UserRegistered>(Arg.Any<UserRegistered>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: "409 — phone_number already exists locally" (pre-check before Cognito).
    /// </summary>
    [Fact]
    public async Task Post_phone_signup_with_duplicate_phone_returns_409()
    {
        await ResetDbAsync();
        ResetMocks();
        const string phone = "+5511988889999";

        // Seed an existing user with the same phone.
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
            ctx.Users.Add(User.CreateForPhone(Guid.NewGuid(), "preexisting-sub", phone, DateTimeOffset.UtcNow));
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/signup",
            new { provider = "phone", phone = new { phoneNumber = phone } },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await _fx.Cognito.DidNotReceiveWithAnyArgs().SignUpPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: "409 — external identity already linked in Cognito".
    /// </summary>
    [Fact]
    public async Task Post_google_signup_with_already_linked_identity_returns_409()
    {
        await ResetDbAsync();
        ResetMocks();
        _fx.Oidc.ValidateAsync(Arg.Any<string>(), OidcProvider.Google, Arg.Any<CancellationToken>())
            .Returns(new OidcClaims("google-sub-dup", "x@example.com", true));
        _fx.Cognito.FindExternalUserSubAsync(IdentityProvider.Google, "google-sub-dup", Arg.Any<CancellationToken>())
            .Returns("existing-cog-sub");

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/signup",
            new { provider = "google", google = new { idToken = "tok" } },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Covers: "422 — Cognito returns InvalidParameterException / CodeDeliveryFailureException
    /// or IdP token missing required claims (mapped to CognitoPolicyViolationException)".
    /// </summary>
    [Fact]
    public async Task Post_phone_signup_when_cognito_policy_violation_returns_422()
    {
        await ResetDbAsync();
        ResetMocks();
        _fx.Cognito.SignUpPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new CognitoPolicyViolationException("SMS delivery failed"));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/signup",
            new { provider = "phone", phone = new { phoneNumber = "+5511977776666" } },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
    }

    /// <summary>
    /// Covers: "502 — unexpected Cognito service error".
    /// </summary>
    [Fact]
    public async Task Post_phone_signup_when_cognito_unavailable_returns_502()
    {
        await ResetDbAsync();
        ResetMocks();
        _fx.Cognito.SignUpPhoneAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new CognitoUnavailableException("boom"));

        var client = _fx.Factory.CreateClient();
        var resp = await client.PostAsJsonAsync(
            "/api/v1/auth/signup",
            new { provider = "phone", phone = new { phoneNumber = "+5511966665555" } },
            TestContext.Current.CancellationToken);

        resp.StatusCode.Should().Be(HttpStatusCode.BadGateway);
    }

    // =====================================================================
    // Fixture: spins up Postgres + WebApplicationFactory once for the class
    // =====================================================================

    public sealed class Fixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
            .Build();

        public ICognitoSignupClient Cognito { get; private set; } = Substitute.For<ICognitoSignupClient>();
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
                            // Minimum to satisfy FA.1 JWT options validators.
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
                        // Replace the Cognito SDK / OIDC validator / event publisher with substitutes.
                        ReplaceService(services, typeof(ICognitoSignupClient), Cognito);
                        ReplaceService(services, typeof(IOidcTokenValidator), Oidc);
                        ReplaceService(services, typeof(IEventPublisher), Publisher);
                    });
                });

            // Apply EF schema to the throwaway Postgres.
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
