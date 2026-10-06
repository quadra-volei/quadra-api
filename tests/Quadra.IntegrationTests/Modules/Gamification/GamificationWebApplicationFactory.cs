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
using Quadra.Modules.Gamification.Abstractions;
using Quadra.Modules.Gamification.Entities;
using Quadra.Modules.Gamification.Persistence;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Testcontainers.PostgreSql;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.IntegrationTests.Modules.Gamification;

/// <summary>
/// Shared test fixture for F2.3 (Group Ranking) integration tests. Spins up a real Postgres
/// (postgis/postgis:16-3.4) via Testcontainers, applies the Auth, Matches and Gamification EF
/// migrations, and swaps the JWT bearer for a static in-process signing key. Matches are seeded
/// directly through the Matches DbContext (the endpoint validates the match through
/// <see cref="Quadra.Shared.Contracts.IMatchGroupReader"/>); standings and the ledger are driven
/// through the Gamification-owned write interfaces (<see cref="IMatchPointsWriter"/> and
/// <see cref="IGroupRankingRepository"/>) — exactly as the Background Worker would.
/// </summary>
public sealed class GamificationWebApplicationFactory : IAsyncLifetime
{
    public const string TestUserPoolId = "us-east-1_GAMIFICATION_TEST";
    public const string TestRegion = "us-east-1";
    public const string TestAudience = "gamification-test-client-id";
    public static string TestIssuer => $"https://cognito-idp.{TestRegion}.amazonaws.com/{TestUserPoolId}";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .Build();

    public IEventPublisher Publisher { get; } = Substitute.For<IEventPublisher>();

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
                        // S3 — required by AddProfileModule fail-fast
                        ["Aws:S3:ProfilePhotosBucket"] = "quadra-profile-photos-test",
                        ["Aws:S3:PhotoUploadUrlTtlMinutes"] = "15",
                        ["Aws:S3:PhotoReadUrlTtlMinutes"] = "60",
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    ReplaceService(services, typeof(IEventPublisher), Publisher);

                    services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, jwt =>
                    {
                        jwt.Authority = null;
                        jwt.MetadataAddress = null!;
                        jwt.RequireHttpsMetadata = false;

                        var staticConfig = new OpenIdConnectConfiguration
                        {
                            Issuer = TestIssuer,
                        };
                        staticConfig.SigningKeys.Add(GamificationTestSigningKeys.PublicKey);

                        jwt.ConfigurationManager =
                            new StaticConfigurationManager<OpenIdConnectConfiguration>(staticConfig);

                        jwt.TokenValidationParameters.ValidIssuer = TestIssuer;
                        jwt.TokenValidationParameters.IssuerSigningKeys = new[] { GamificationTestSigningKeys.PublicKey };
                        jwt.TokenValidationParameters.ValidateIssuerSigningKey = true;
                        jwt.TokenValidationParameters.ValidateIssuer = true;
                        jwt.TokenValidationParameters.ValidateLifetime = true;
                        jwt.TokenValidationParameters.ValidateAudience = false;
                    });
                });
            });

        using var scope = Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Database.MigrateAsync();
        await scope.ServiceProvider.GetRequiredService<GamificationDbContext>().Database.MigrateAsync();
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
        var token = GamificationTestJwtFactory.CreateAccessToken(TestIssuer, TestAudience, userId.ToString());
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>Creates an <see cref="HttpClient"/> with a syntactically valid but wrongly-signed JWT.</summary>
    public HttpClient CreateClientWithInvalidToken()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "not-a-real-token");
        return client;
    }

    /// <summary>Creates an unauthenticated <see cref="HttpClient"/>.</summary>
    public HttpClient CreateAnonymousClient() => Factory.CreateClient();

    /// <summary>Seeds a match row of the given type and returns its id (== the group id in the MVP).</summary>
    public async Task<Guid> SeedMatchAsync(MatchType type, string name = "Sunday Volleyball")
    {
        using var scope = Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var now = DateTimeOffset.UtcNow;
        var match = Match.Create(
            organizerId: Guid.NewGuid(),
            name: name,
            description: null,
            address: "Court 1",
            latitude: -23.5,
            longitude: -46.6,
            dateTime: now.AddDays(1),
            maxPlayers: 12,
            regularSlots: 12,
            price: null,
            type: type,
            frequency: type == MatchType.Recurring ? MatchFrequency.Weekly : null,
            dayOfWeekIso: type == MatchType.Recurring ? 7 : null,
            windowOpensAt: now,
            windowClosesAt: now.AddHours(12),
            now: now);
        ctx.Matches.Add(match);
        await ctx.SaveChangesAsync();
        return match.Id;
    }

    /// <summary>Applies a finished match's points through the Gamification write interface (as the Worker would).</summary>
    public async Task ApplyPointsAsync(FinishedMatchPoints points)
    {
        using var scope = Factory.Services.CreateScope();
        var writer = scope.ServiceProvider.GetRequiredService<IMatchPointsWriter>();
        await writer.ApplyFinishedMatchPointsAsync(points, CancellationToken.None);
    }

    /// <summary>Seeds a materialized standing row directly through the Gamification write interface.</summary>
    public async Task SeedRankingAsync(
        Guid groupId,
        Guid userId,
        int totalPoints,
        int matchesCounted = 1,
        DateTimeOffset? lastMatchDateTime = null)
    {
        using var scope = Factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IGroupRankingRepository>();
        await repo.UpsertAsync(
            groupId,
            userId,
            totalPoints,
            matchesCounted,
            Guid.NewGuid(),
            lastMatchDateTime ?? DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow,
            CancellationToken.None);
    }

    /// <summary>Reads a standing row directly from the Gamification tables for assertions.</summary>
    public async Task<GroupRanking?> GetRankingRowAsync(Guid groupId, Guid userId)
    {
        using var scope = Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GamificationDbContext>();
        return await ctx.GroupRankings
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.GroupId == groupId && r.UserId == userId);
    }

    /// <summary>Counts ledger rows for a match (optionally filtered by user) for assertions.</summary>
    public async Task<int> CountTransactionsAsync(Guid matchId, Guid? userId = null)
    {
        using var scope = Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<GamificationDbContext>();
        var query = ctx.PointTransactions.AsNoTracking().Where(t => t.MatchId == matchId);
        if (userId is { } uid)
        {
            query = query.Where(t => t.UserId == uid);
        }

        return await query.CountAsync();
    }

    /// <summary>Truncates the Matches and Gamification tables so each test starts from a clean slate.</summary>
    public async Task ResetAsync()
    {
        using var scope = Factory.Services.CreateScope();
        var gamCtx = scope.ServiceProvider.GetRequiredService<GamificationDbContext>();
        await gamCtx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE point_transactions, group_rankings RESTART IDENTITY CASCADE;");
        var matchesCtx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await matchesCtx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE matches RESTART IDENTITY CASCADE;");
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
