using System.Net;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Waiting list and drop-in slots keep the match within its slots and never block a decline.
/// Found by the 2026-11-24 audit with the Cloudflare security-audit skill; each test asserts the secure behavior.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class QueueAndSlotsAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public QueueAndSlotsAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

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

    // 5. quadra-api:matches:waitlist-promotion-duplicate-presence
    [Fact]
    public async Task A_waiter_with_a_declined_row_does_not_block_declines()
    {
        var a = await _fx.LoginAsync();
        var b = await _fx.LoginAsync();
        var c = await _fx.LoginAsync();
        var matchId = await CreateMatchAsync(a, "Fila travada", max: 2, regular: 2);
        var me = $"/api/v1/matches/{matchId}/presences/me";

        async Task<HttpStatusCode> Put(Actor x, string status) => (await x.Client.PutAsJsonAsync(me, new { status }, Ct)).StatusCode;
        var aStatus = await Put(a, "Confirmed");                      // creator may already be confirmed
        (await Put(c, "Confirmed")).Should().Be(HttpStatusCode.OK);   // 2/2
        (await Put(c, "Declined")).Should().Be(HttpStatusCode.OK);    // C keeps a Declined row
        (await Put(b, "Confirmed")).Should().Be(HttpStatusCode.OK);   // 2/2 again
        var cAgain = await Put(c, "Confirmed");                       // full: C goes to the waiting list
        cAgain.Should().Be(HttpStatusCode.Conflict, $"(creator confirm answered {(int)aStatus}) C should be queued");

        var bDecline = await b.Client.PutAsJsonAsync(me, new { status = "Declined" }, Ct);
        bDecline.StatusCode.Should().Be(HttpStatusCode.OK,
            $"B could not decline while C (with an old presence row) heads the waiting list: {await bDecline.Content.ReadAsStringAsync(Ct)}");
    }

    // quadra-api:matches:released-dropin-slots-ignore-guests
    [Fact]
    public async Task Released_drop_in_slots_never_pass_max_players_with_guests()
    {
        var a = await _fx.LoginAsync();
        var c = await _fx.LoginAsync();
        var d = await _fx.LoginAsync();
        var matchId = await CreateMatchAsync(a, "Vagas com convidados", max: 4, regular: 4);
        var me = $"/api/v1/matches/{matchId}/presences/me";
        await a.Client.PutAsJsonAsync(me, new { status = "Confirmed" }, Ct);
        foreach (var name in new[] { "Convidado 1", "Convidado 2" })
        {
            (await a.Client.PostAsJsonAsync($"/api/v1/matches/{matchId}/guests", new { name, position = (string?)null }, Ct))
                .IsSuccessStatusCode.Should().BeTrue();
        }

        // Close the confirmation window now; the next read of the match applies it.
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
            var match = await db.Matches.SingleAsync(m => m.Id == matchId, Ct);
            db.Entry(match).Property(nameof(match.WindowClosesAt)).CurrentValue = DateTimeOffset.UtcNow.AddMinutes(-1);
            await db.SaveChangesAsync(Ct);
        }

        await a.Client.GetAsync($"/api/v1/matches/{matchId}/detail", Ct);

        // 1 confirmed + 2 guests of MaxPlayers 4: one drop-in spot is left, not three.
        (await c.Client.PutAsJsonAsync(me, new { status = "Confirmed" }, Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await d.Client.PutAsJsonAsync(me, new { status = "Confirmed" }, Ct)).StatusCode
            .Should().Be(HttpStatusCode.Conflict, "the roster would pass MaxPlayers (1 + 2 guests + 2 drop-ins > 4)");
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
