using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quadra.Modules.Auth.Persistence;

namespace Quadra.IntegrationTests.Modules.Auth;

/// <summary>
/// Spins up the real Quadra.Api host in-memory with the test <c>Auth:Jwt</c> settings, so tests
/// can sign tokens locally with <see cref="TestJwtFactory"/> and exercise the real bearer
/// middleware. No database or network is touched by the JWT-only tests.
/// </summary>
public sealed class AuthWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(TestJwtFactory.AuthSettings());
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                // Matches module fail-fast: SQS queue URLs must be present at startup.
                ["Aws:Sqs:MatchCreatedQueueUrl"] = "http://test-stub/match-created",
                ["Aws:Sqs:MatchStatusChangedQueueUrl"] = "http://test-stub/match-status-changed",
                // F1.2 presence management SQS queues.
                ["Aws:Sqs:PresenceConfirmedQueueUrl"] = "http://test-stub/presence-confirmed",
                ["Aws:Sqs:MatchWindowOpenedQueueUrl"] = "http://test-stub/match-window-opened",
                ["Aws:Sqs:MatchWindowClosedQueueUrl"] = "http://test-stub/match-window-closed",
                // F1.3 in-game teams SQS queue.
                ["Aws:Sqs:TeamsFormedQueueUrl"] = "http://test-stub/teams-formed",
                // Database: must be present; use in-memory-safe stub for JWT-only tests.
                ["ConnectionStrings:Default"] = "Host=localhost;Port=5432;Database=quadra_test_stub;Username=stub;Password=stub",
            });
        });

        builder.ConfigureTestServices(services =>
        {
            // Register the test controllers (TestProtectedController, TestHubProtectedController, …).
            services
                .AddControllers()
                .AddApplicationPart(typeof(TestProtectedController).Assembly);

            // No database here: every session counts as active, so these tests exercise only the
            // token checks. The session check itself is covered by SecurityAudit/SessionAuditTests.
            var sessions = Substitute.For<IRefreshTokenRepository>();
            sessions.IsSessionActiveAsync(default, default, default, default).ReturnsForAnyArgs(true);
            services.AddScoped(_ => sessions);
        });
    }
}
