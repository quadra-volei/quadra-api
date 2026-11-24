using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Notifications;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Self-service accounts cannot run up operator cost or fill shared work: push tokens per account,
/// the daily Places budget and new matches per organizer are bounded.
/// Found by the 2026-11-24 audit with the Cloudflare security-audit skill; each test asserts the secure behavior.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class CostLimitsAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public CostLimitsAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // quadra-api:notifications:push-token-per-account-cardinality
    [Fact]
    public async Task An_account_keeps_only_its_most_recent_push_tokens()
    {
        var p = await _fx.LoginAsync();
        for (var i = 0; i < 20; i++)
        {
            (await p.Client.PutAsJsonAsync("/api/v1/notifications/push-token", new { token = $"ExponentPushToken[audit{i:D4}x]" }, Ct))
                .IsSuccessStatusCode.Should().BeTrue();
        }

        using var scope = _fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<NotificationsDbContext>();
        db.PushTokens.Count(t => t.UserId == p.Id).Should().BeLessThanOrEqualTo(NotificationsHandler.MaxPushTokensPerUser);
    }

    // quadra-api:geo:places-spend-per-account-only-quota
    [Fact]
    public async Task Places_calls_share_one_daily_budget_across_accounts()
    {
        using var host = _fx.Create("Testing", new Dictionary<string, string?> { ["Places:MaxRequestsPerDay"] = "3" });
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            var account = await _fx.LoginAsync(host);
            for (var j = 0; j < 2; j++)
            {
                codes.Add((await account.Client.GetAsync($"/api/v1/places/autocomplete?q=rua+teste+{i}{j}", Ct)).StatusCode);
            }
        }

        codes.Count(c => c == HttpStatusCode.TooManyRequests).Should().Be(3, $"3 accounts x 2 calls over a budget of 3 (got {string.Join(',', codes)})");
    }

    // quadra-api:background:abandoned-sweep-fixed-batch-self-renewing-recurring-starvation (creation side)
    [Fact]
    public async Task An_organizer_cannot_create_unbounded_matches_per_day()
    {
        using var host = _fx.Create("Testing", new Dictionary<string, string?> { ["Matches:CreatePerDay"] = "2" });
        var a = await _fx.LoginAsync(host);
        var codes = new List<HttpStatusCode>();
        for (var i = 0; i < 3; i++)
        {
            codes.Add((await a.Client.PostAsJsonAsync("/api/v1/matches", new
            {
                name = $"Racha {i}", address = "Rua A", latitude = -23.5, longitude = -46.6, dateTime = DateTimeOffset.UtcNow.AddDays(2),
                maxPlayers = 8, regularSlots = 8, type = "OneOff", confirmationOpensHoursBefore = 96,
            }, Ct)).StatusCode);
        }

        codes.Should().Equal(HttpStatusCode.Created, HttpStatusCode.Created, HttpStatusCode.TooManyRequests);
    }
}
