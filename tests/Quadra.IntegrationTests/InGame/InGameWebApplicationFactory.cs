using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quadra.Infrastructure.Messaging;
using Quadra.IntegrationTests.Modules.Auth;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.InGame.Persistence;
using Quadra.Modules.Matches.Persistence;
using Quadra.Modules.Profile.Persistence;
using Quadra.Shared.Realtime;
using Testcontainers.PostgreSql;

namespace Quadra.IntegrationTests.InGame;

/// <summary>
/// Shared test fixture for F1.3 (In-Game Teams) integration tests.
/// Spins up a real Postgres (postgis/postgis:16-3.4) via Testcontainers,
/// applies Auth, Matches, and InGame EF migrations, configures the test
/// <c>Auth:Jwt</c> signing key, and provides an
/// <see cref="IEventPublisher"/> substitute so tests can verify event publishing.
/// </summary>
public sealed class InGameWebApplicationFactory : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
        .Build();

    public IEventPublisher Publisher { get; private set; } = Substitute.For<IEventPublisher>();

    public IMatchRoomNotifier Notifier { get; private set; } = Substitute.For<IMatchRoomNotifier>();

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
                    // Auth module options (own JWT, fake phone verification)
                    config.AddInMemoryCollection(TestJwtFactory.AuthSettings());
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
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
                    });
                });

                builder.ConfigureTestServices(services =>
                {
                    // Replace IEventPublisher with a substitute so tests can verify event calls.
                    ReplaceService(services, typeof(IEventPublisher), Publisher);

                    // Replace IMatchRoomNotifier with a substitute so tests can verify SignalR broadcasts.
                    ReplaceService(services, typeof(IMatchRoomNotifier), Notifier);
                });
            });

        // Apply EF migrations for all modules to the Testcontainers Postgres instance.
        using var scope = Factory.Services.CreateScope();
        var authCtx = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        await authCtx.Database.MigrateAsync();
        var matchesCtx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await matchesCtx.Database.MigrateAsync();
        var inGameCtx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();
        await inGameCtx.Database.MigrateAsync();
        // F2.1: Program.cs now wires the real PlayerLevelReader (over the F1.3 NoOp), which reads
        // player_profiles during team drafting — so the Profile schema must exist for these tests.
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
        var token = TestJwtFactory.CreateAccessToken(subject: userId.ToString());
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
