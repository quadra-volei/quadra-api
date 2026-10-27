using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quadra.IntegrationTests.Modules.Auth;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Gamification.Persistence;
using Quadra.Modules.InGame.Persistence;
using Quadra.Modules.Matches.Persistence;
using Quadra.Modules.Profile.Persistence;
using Testcontainers.PostgreSql;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Security audit fixture: the whole API as it runs on Render (real in-process events, real
/// SignalR notifier, all five schemas) against a real PostGIS. Nothing is substituted, so the
/// tests see what a real client would see. Users log in through the fake SMS provider, like the
/// hosted environment.
/// </summary>
public sealed class AuditWebApplicationFactory : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4").Build();

    public WebApplicationFactory<Program> Factory { get; private set; } = default!;

    public string ConnectionString => _postgres.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();
        Factory = Create("Testing", new Dictionary<string, string?>());

        using var scope = Factory.Services.CreateScope();
        var sp = scope.ServiceProvider;
        await sp.GetRequiredService<AuthDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<Quadra.Modules.Notifications.NotificationsDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<MatchesDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<InGameDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<ProfileDbContext>().Database.MigrateAsync();
        await sp.GetRequiredService<GamificationDbContext>().Database.MigrateAsync();
    }

    /// <summary>A host on the same database with another environment and extra settings.</summary>
    public WebApplicationFactory<Program> Create(string environment, IDictionary<string, string?> overrides) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment(environment);
            // UseSetting, not ConfigureAppConfiguration: Program reads several keys while registering
            // services, before late configuration sources are applied.
            var settings = new Dictionary<string, string?>(TestJwtFactory.AuthSettings())
            {
                ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                ["Places:Provider"] = "None",
                ["Matches:WindowSweepSeconds"] = "0",
                // The season pass is being played, with room for four capped weeks before today.
                ["SeasonPass:Seasons:T1:StartsOn"] = $"{DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)).AddDays(-35):yyyy-MM-dd}",
                ["SeasonPass:Seasons:T1:EndsOn"] = $"{DateOnly.FromDateTime(DateTime.UtcNow.AddHours(-3)).AddDays(60):yyyy-MM-dd}",
            };
            foreach (var queue in new[]
                     {
                         "MatchCreated", "MatchStatusChanged", "PresenceConfirmed", "MatchWindowOpened", "MatchWindowClosed",
                         "MatchSummaryGenerated", "TeamsFormed", "MatchStarted", "MatchEnded", "MvpAwarded",
                     })
            {
                settings[$"Aws:Sqs:{queue}QueueUrl"] = $"http://unused/{queue}";
            }

            foreach (var (key, value) in overrides)
            {
                settings[key] = value;
            }

            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }
        });

    public async ValueTask DisposeAsync()
    {
        Factory.Dispose();
        await _postgres.DisposeAsync();
    }

    private static int _phoneSeq = 1000000;

    /// <summary>Logs a new phone in through the public SMS endpoint; returns the account and a client carrying its token.</summary>
    public async Task<Actor> LoginAsync(WebApplicationFactory<Program>? host = null)
    {
        var phone = $"+55119{Interlocked.Increment(ref _phoneSeq):D8}";
        var client = (host ?? Factory).CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "verify", phoneNumber = phone, code = TestJwtFactory.FakeOtpCode },
            TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var access = body.GetProperty("accessToken").GetString()!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        return new Actor(body.GetProperty("userId").GetGuid(), phone, access, body.GetProperty("refreshToken").GetString()!, client);
    }
}

public sealed record Actor(Guid Id, string Phone, string AccessToken, string RefreshToken, HttpClient Client);
