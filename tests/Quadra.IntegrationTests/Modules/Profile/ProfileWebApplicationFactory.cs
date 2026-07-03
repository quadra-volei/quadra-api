using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NSubstitute;
using Quadra.Infrastructure.Messaging;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Profile.Persistence;
using Testcontainers.PostgreSql;

namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>
/// Shared test fixture for F2.1 (Player Profile) integration tests. Spins up a real Postgres
/// (postgis/postgis:16-3.4) via Testcontainers, applies the Auth and Profile EF migrations, swaps
/// the JWT bearer for a static in-process signing key, and replaces <see cref="IProfilePhotoStorage"/>
/// with a substitute so no real S3 call is made (deterministic presigned-URL stand-ins).
/// </summary>
public sealed class ProfileWebApplicationFactory : IAsyncLifetime
{
    public const string TestUserPoolId = "us-east-1_PROFILE_TEST";
    public const string TestRegion = "us-east-1";
    public const string TestAudience = "profile-test-client-id";
    public static string TestIssuer => $"https://cognito-idp.{TestRegion}.amazonaws.com/{TestUserPoolId}";

    /// <summary>Deterministic stand-in URLs returned by the photo-storage substitute.</summary>
    public const string StubUploadUrl = "https://s3.test.local/upload";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .Build();

    public IEventPublisher Publisher { get; } = Substitute.For<IEventPublisher>();

    public IProfilePhotoStorage PhotoStorage { get; } = Substitute.For<IProfilePhotoStorage>();

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        // The photo-storage substitute returns deterministic URLs (no real S3 in tests).
        PhotoStorage
            .CreateUploadUrlAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new PhotoUploadTarget(StubUploadUrl, DateTimeOffset.UtcNow.AddMinutes(15)));
        PhotoStorage
            .GetReadUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(call => $"https://s3.test.local/read/{call.ArgAt<string>(0)}");

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                builder.ConfigureAppConfiguration((_, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        // Auth module options
                        ["Auth:Cognito:UserPoolId"] = TestUserPoolId,
                        ["Auth:Cognito:Region"] = TestRegion,
                        ["Auth:Cognito:Audience"] = TestAudience,
                        ["Auth:Cognito:AppClientId"] = TestAudience,
                        ["Auth:Cognito:ClockSkewSeconds"] = "30",
                        ["Auth:Google:ClientId"] = "test-google-client",
                        ["Auth:Google:Issuer"] = "https://accounts.google.com",
                        ["Auth:Google:JwksUri"] = "https://accounts.google.com/.well-known/openid-configuration",
                        ["Auth:Apple:ClientId"] = "test-apple-client",
                        ["Auth:Apple:Issuer"] = "https://appleid.apple.com",
                        ["Auth:Apple:JwksUri"] = "https://appleid.apple.com/.well-known/openid-configuration",
                        // Database — all modules share the same container
                        ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                        // SQS — required by AddMatchesModule fail-fast
                        ["Aws:Sqs:MatchCreatedQueueUrl"] = "http://localhost:4566/000000000000/match-created",
                        ["Aws:Sqs:MatchStatusChangedQueueUrl"] = "http://localhost:4566/000000000000/match-status-changed",
                        ["Aws:Sqs:PresenceConfirmedQueueUrl"] = "http://localhost:4566/000000000000/presence-confirmed",
                        ["Aws:Sqs:MatchWindowOpenedQueueUrl"] = "http://localhost:4566/000000000000/match-window-opened",
                        ["Aws:Sqs:MatchWindowClosedQueueUrl"] = "http://localhost:4566/000000000000/match-window-closed",
                        // SQS — required by AddInGameModule fail-fast
                        ["Aws:Sqs:TeamsFormedQueueUrl"] = "http://localhost:4566/000000000000/teams-formed",
                        ["Aws:Sqs:MatchStartedQueueUrl"] = "http://localhost:4566/000000000000/match-started",
                        ["Aws:Sqs:MatchEndedQueueUrl"] = "http://localhost:4566/000000000000/match-ended",
                        ["Aws:Sqs:MvpAwardedQueueUrl"] = "http://localhost:4566/000000000000/mvp-awarded",
                        // S3 — required by AddProfileModule (AddProfilePhotoStorage) fail-fast
                        ["Aws:S3:ProfilePhotosBucket"] = "quadra-profile-photos-test",
                        ["Aws:S3:PhotoUploadUrlTtlMinutes"] = "15",
                        ["Aws:S3:PhotoReadUrlTtlMinutes"] = "60",
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    ReplaceService(services, typeof(IEventPublisher), Publisher);
                    ReplaceService(services, typeof(IProfilePhotoStorage), PhotoStorage);

                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
                    {
                        jwt.Authority = null;
                        jwt.MetadataAddress = null!;
                        jwt.RequireHttpsMetadata = false;

                        var staticConfig = new OpenIdConnectConfiguration
                        {
                            Issuer = TestIssuer,
                        };
                        staticConfig.SigningKeys.Add(ProfileTestSigningKeys.PublicKey);

                        jwt.ConfigurationManager =
                            new StaticConfigurationManager<OpenIdConnectConfiguration>(staticConfig);

                        jwt.TokenValidationParameters.ValidIssuer = TestIssuer;
                        jwt.TokenValidationParameters.IssuerSigningKeys = new[] { ProfileTestSigningKeys.PublicKey };
                        jwt.TokenValidationParameters.ValidateIssuerSigningKey = true;
                        jwt.TokenValidationParameters.ValidateIssuer = true;
                        jwt.TokenValidationParameters.ValidateLifetime = true;
                        jwt.TokenValidationParameters.ValidateAudience = false;
                    });
                });
            });

        using var scope = Factory.Services.CreateScope();
        var authCtx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await authCtx.Database.MigrateAsync();
        var profileCtx = scope.ServiceProvider.GetRequiredService<ProfileDbContext>();
        await profileCtx.Database.MigrateAsync();
    }

    public async ValueTask DisposeAsync()
    {
        Factory.Dispose();
        await _postgres.DisposeAsync();
    }

    /// <summary>Creates an <see cref="HttpClient"/> with a valid JWT for the given user id.</summary>
    public HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var client = Factory.CreateClient();
        var token = ProfileTestJwtFactory.CreateAccessToken(TestIssuer, TestAudience, userId.ToString());
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Creates an unauthenticated <see cref="HttpClient"/>.</summary>
    public HttpClient CreateAnonymousClient() => Factory.CreateClient();

    /// <summary>Runs an action within a fresh DI scope (for seeding via Profile-owned write interfaces).</summary>
    public async Task WithScopeAsync(Func<IServiceProvider, Task> action)
    {
        using var scope = Factory.Services.CreateScope();
        await action(scope.ServiceProvider);
    }

    /// <summary>Truncates the Profile tables so each test starts from a clean slate.</summary>
    public async Task ResetAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<ProfileDbContext>();
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE player_match_history, player_stats, player_profiles RESTART IDENTITY CASCADE;");
        PhotoStorage.ClearReceivedCalls();
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
