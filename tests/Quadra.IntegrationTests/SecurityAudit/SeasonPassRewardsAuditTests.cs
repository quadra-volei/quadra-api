using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NSubstitute;
using Quadra.Modules.Gamification.Billing;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Season pass rewards stay locked to who unlocked them.
/// Found by the 2026-11-24 audit with the Cloudflare security-audit skill; each test asserts the secure behavior.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class SeasonPassRewardsAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public SeasonPassRewardsAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static object MatchBody(string name, DateTimeOffset when, int max, int regular, string visibility = "Open") => new
    {
        name, address = "Rua Teste, 1", latitude = -23.56, longitude = -46.65, dateTime = when,
        maxPlayers = max, regularSlots = regular, type = "OneOff", visibility, inviteMode = visibility == "Private" ? "Code" : null,
        confirmationOpensHoursBefore = 96,
    };

    private async Task<Guid> CreateMatchAsync(Actor organizer, string name, int max = 12, int regular = 12, string visibility = "Open")
    {
        var r = await organizer.Client.PostAsJsonAsync("/api/v1/matches", MatchBody(name, DateTimeOffset.UtcNow.AddDays(2), max, regular, visibility), Ct);
        r.StatusCode.Should().Be(HttpStatusCode.Created, await r.Content.ReadAsStringAsync(Ct));
        return (await r.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private Task<HttpResponseMessage> MakePrivateAsync(Actor organizer, Guid matchId, string name) =>
        organizer.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}", new
        {
            name, description = (string?)null, address = "Rua Teste, 1", latitude = -23.56, longitude = -46.65,
            dateTime = DateTimeOffset.UtcNow.AddDays(2), maxPlayers = 12, regularSlots = 12, price = (decimal?)null,
            visibility = "Private",
        }, Ct);

    // 2. quadra-api:profile:card-style-season-finish-lock-bypass
    [Theory]
    [InlineData("Ion,Eclipse")]
    [InlineData("Pioneira ")]
    public async Task Locked_season_finish_cannot_be_equipped_with_a_non_canonical_name(string finish)
    {
        var p = await _fx.LoginAsync();
        var r = await p.Client.PutAsJsonAsync("/api/v1/profiles/me/card-style", new { finish, title = (string?)null, photoObjectKey = (string?)null }, Ct);
        var me = await (await p.Client.GetAsync("/api/v1/profiles/me", Ct)).Content.ReadAsStringAsync(Ct);
        me.Should().NotContain("\"Pioneira\"", $"PUT card-style '{finish}' answered {(int)r.StatusCode} and the locked finish was stored");
    }

    // quadra-api:gamification:voided-purchase-sweep-window-revocation-gap
    [Fact]
    public async Task Refunds_are_swept_as_soon_as_the_api_starts()
    {
        var play = Substitute.For<IGooglePlayPurchases>();
        play.ListVoidedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>()).Returns([]);
        using var host = _fx.Create("Testing", new Dictionary<string, string?>
        {
            ["SeasonPass:Billing:ServiceAccountJson"] = "{}",
            ["SeasonPass:Billing:VoidedSweepMinutes"] = "60",
        }).WithWebHostBuilder(b => b.ConfigureTestServices(s =>
        {
            s.RemoveAll<IGooglePlayPurchases>();
            s.AddSingleton(play);
        }));
        host.CreateClient(); // starts the host and its hosted services

        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (play.ReceivedCalls().All(c => c.GetMethodInfo().Name != nameof(IGooglePlayPurchases.ListVoidedAsync))
               && DateTime.UtcNow < deadline)
        {
            await Task.Delay(200, Ct);
        }

        await play.Received().ListVoidedAsync(Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
    }

    private static Task SendFrameAsync(WebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json + "\u001e"), WebSocketMessageType.Text, true, Ct);

    private static async Task<string> ReceiveFramesAsync(WebSocket socket, TimeSpan window)
    {
        var sb = new StringBuilder();
        var buffer = new byte[16 * 1024];
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        cts.CancelAfter(window);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await socket.ReceiveAsync(buffer, cts.Token);
                sb.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
            }
        }
        catch (OperationCanceledException)
        {
        }

        return sb.ToString();
    }
}
