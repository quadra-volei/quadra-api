using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// An invite link carries a private match's code (DECISIONS #73): with the right code a stranger
/// sees the match detail before joining; without it, or with a wrong one, the match stays hidden.
/// The code never reaches non-organizers and opens no other read.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class InviteLinkAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public InviteLinkAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Only_the_right_invite_code_shows_a_private_match_to_a_stranger()
    {
        var organizer = await _fx.LoginAsync();
        var stranger = await _fx.LoginAsync();
        var created = await organizer.Client.PostAsJsonAsync("/api/v1/matches", new
        {
            name = "Racha fechado", address = "Rua A", latitude = -23.5, longitude = -46.6,
            dateTime = DateTimeOffset.UtcNow.AddDays(3), maxPlayers = 12, regularSlots = 12,
            type = "OneOff", confirmationOpensHoursBefore = 96, visibility = "Private", inviteMode = "Code",
        }, Ct);
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var matchId = (await created.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
        var code = (await organizer.Client.GetFromJsonAsync<JsonElement>($"/api/v1/matches/{matchId}/detail", Ct))
            .GetProperty("inviteCode").GetString()!;

        (await stranger.Client.GetAsync($"/api/v1/matches/{matchId}/detail", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Client.GetAsync($"/api/v1/matches/{matchId}/detail?inviteCode=WRONG234", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var seen = await stranger.Client.GetAsync($"/api/v1/matches/{matchId}/detail?inviteCode={code.ToLowerInvariant()}", Ct);
        seen.StatusCode.Should().Be(HttpStatusCode.OK);
        (await seen.Content.ReadAsStringAsync(Ct)).Should().NotContain(code, "the code stays with the organizer");

        // The code opens the detail only; the other reads still need membership.
        (await stranger.Client.GetAsync($"/api/v1/matches/{matchId}/presences?inviteCode={code}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Joining still takes the code, as before.
        (await stranger.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed", inviteCode = code }, Ct))
            .IsSuccessStatusCode.Should().BeTrue();
        (await stranger.Client.GetAsync($"/api/v1/matches/{matchId}/detail", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
