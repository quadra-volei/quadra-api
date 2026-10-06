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
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Matches.Persistence;
using Testcontainers.PostgreSql;

namespace Quadra.IntegrationTests.Matches;

/// <summary>
/// Shared test fixture for F1.1 integration tests.
/// Spins up a real Postgres (postgis/postgis:16-3.4) via Testcontainers,
/// applies both Auth and Matches EF migrations, replaces the JWT bearer
/// with a static in-process signing key, and provides an
/// <see cref="IEventPublisher"/> substitute so tests can verify event publishing.
/// </summary>
public sealed class MatchesWebApplicationFactory : IAsyncLifetime
{
    public const string TestUserPoolId = "us-east-1_MATCHES_TEST";
    public const string TestRegion = "us-east-1";
    public const string TestAudience = "matches-test-client-id";
    public static string TestIssuer => $"https://cognito-idp.{TestRegion}.amazonaws.com/{TestUserPoolId}";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .Build();

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
                        // Auth module options (required by AddAuthModule ValidateOnStart)
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
                        // Database — both modules share the same container
                        ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                        // SQS (required by AddMatchesModule fail-fast)
                        ["Aws:Sqs:MatchCreatedQueueUrl"] = "http://localhost:4566/000000000000/match-created",
                        ["Aws:Sqs:MatchStatusChangedQueueUrl"] = "http://localhost:4566/000000000000/match-status-changed",
                        ["Aws:Sqs:PresenceConfirmedQueueUrl"] = "http://localhost:4566/000000000000/presence-confirmed",
                        ["Aws:Sqs:MatchWindowOpenedQueueUrl"] = "http://localhost:4566/000000000000/match-window-opened",
                        ["Aws:Sqs:MatchWindowClosedQueueUrl"] = "http://localhost:4566/000000000000/match-window-closed",
                        // SQS (required by AddInGameModule fail-fast — registered in Program.cs)
                        ["Aws:Sqs:TeamsFormedQueueUrl"] = "http://localhost:4566/000000000000/teams-formed",
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    // Replace IEventPublisher with a substitute so tests can verify event calls.
                    ReplaceService(services, typeof(IEventPublisher), Publisher);

                    // Replace JWT bearer OIDC discovery with a static in-process signing key.
                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
                    {
                        jwt.Authority = null;
                        jwt.MetadataAddress = null!;
                        jwt.RequireHttpsMetadata = false;

                        var staticConfig = new OpenIdConnectConfiguration
                        {
                            Issuer = TestIssuer,
                        };
                        staticConfig.SigningKeys.Add(MatchesTestSigningKeys.PublicKey);

                        jwt.ConfigurationManager =
                            new StaticConfigurationManager<OpenIdConnectConfiguration>(staticConfig);

                        jwt.TokenValidationParameters.ValidIssuer = TestIssuer;
                        jwt.TokenValidationParameters.IssuerSigningKeys = new[] { MatchesTestSigningKeys.PublicKey };
                        jwt.TokenValidationParameters.ValidateIssuerSigningKey = true;
                        jwt.TokenValidationParameters.ValidateIssuer = true;
                        jwt.TokenValidationParameters.ValidateLifetime = true;
                        jwt.TokenValidationParameters.ValidateAudience = false;
                    });
                });
            });

        // Apply EF migrations to the Testcontainers Postgres instance.
        using var scope = Factory.Services.CreateScope();
        var authCtx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await authCtx.Database.MigrateAsync();
        var matchesCtx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await matchesCtx.Database.MigrateAsync();
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
        var token = MatchesTestJwtFactory.CreateAccessToken(TestIssuer, TestAudience, userId.ToString());
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Creates an unauthenticated <see cref="HttpClient"/>.</summary>
    public HttpClient CreateAnonymousClient() => Factory.CreateClient();

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
