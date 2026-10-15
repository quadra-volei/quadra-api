using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Only the organizer edits (PUT /matches/{id}) or cancels (DELETE /matches/{id}) a match, and
/// only until its game starts. A stranger to a private match gets 404, as for reads (CanViewMatch).
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class MatchEditAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public MatchEditAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stranger_cannot_edit_or_cancel_a_private_match()
    {
        var organizer = await _fx.LoginAsync();
        var stranger = await _fx.LoginAsync();
        var matchId = await CreateAsync(organizer, DateTimeOffset.UtcNow.AddDays(3), visibility: "Private");

        (await stranger.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}", Edit("Hackeada", DateTimeOffset.UtcNow.AddDays(4)), Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await stranger.Client.DeleteAsync($"/api/v1/matches/{matchId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var match = await Read(await organizer.Client.GetAsync($"/api/v1/matches/{matchId}", Ct));
        match.GetProperty("name").GetString().Should().Be("Racha");
        match.GetProperty("status").GetString().Should().NotBe("Cancelled");
    }

    [Fact]
    public async Task Confirmed_player_who_is_not_the_organizer_is_refused()
    {
        var organizer = await _fx.LoginAsync();
        var player = await _fx.LoginAsync();
        var matchId = await CreateAsync(organizer, DateTimeOffset.UtcNow.AddDays(3));
        await Confirm(player, matchId);

        (await player.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}", Edit("Minha agora", DateTimeOffset.UtcNow.AddDays(4)), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await player.Client.DeleteAsync($"/api/v1/matches/{matchId}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Organizer_edits_and_confirmations_are_kept()
    {
        var organizer = await _fx.LoginAsync();
        var player = await _fx.LoginAsync();
        var matchId = await CreateAsync(organizer, DateTimeOffset.UtcNow.AddDays(3));
        await Confirm(player, matchId);
        var newDate = DateTimeOffset.UtcNow.AddDays(5);

        var edited = await Read(await organizer.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}", Edit("Racha de sábado", newDate), Ct));

        edited.GetProperty("name").GetString().Should().Be("Racha de sábado");
        edited.GetProperty("dateTime").GetDateTimeOffset().Should().BeCloseTo(newDate, TimeSpan.FromSeconds(1));
        var detail = await Read(await player.Client.GetAsync($"/api/v1/matches/{matchId}/detail", Ct));
        detail.GetProperty("myPresence").GetProperty("status").GetString().Should().Be("Confirmed");
    }

    [Fact]
    public async Task Slots_cannot_drop_below_confirmed_players()
    {
        var organizer = await _fx.LoginAsync();
        var matchId = await CreateAsync(organizer, DateTimeOffset.UtcNow.AddDays(3));
        await Confirm(organizer, matchId);
        await Confirm(await _fx.LoginAsync(), matchId);
        await Confirm(await _fx.LoginAsync(), matchId);

        var response = await organizer.Client.PutAsJsonAsync(
            $"/api/v1/matches/{matchId}", Edit("Racha", DateTimeOffset.UtcNow.AddDays(3), maxPlayers: 2), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Nothing_changes_once_the_game_started()
    {
        var organizer = await _fx.LoginAsync();
        var kickoff = DateTimeOffset.UtcNow.AddMinutes(10); // within the kickoff tolerance
        var matchId = await CreateAsync(organizer, kickoff);
        await Confirm(organizer, matchId);
        await Confirm(await _fx.LoginAsync(), matchId);
        var b = $"/api/v1/matches/{matchId}";
        var teams = (await Read(await organizer.Client.PostAsJsonAsync($"{b}/teams/draft", new { teamCount = 2, mode = "Random" }, Ct)))
            .GetProperty("teams").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToArray();
        await Read(await organizer.Client.PostAsJsonAsync($"{b}/scoreboard", new { format = "BestOf3", teamAId = teams[0], teamBId = teams[1] }, Ct),
            HttpStatusCode.Created);
        await Read(await organizer.Client.PostAsync($"{b}/scoreboard/start", null, Ct));

        (await organizer.Client.PutAsJsonAsync(b, Edit("Outra", kickoff.AddDays(1)), Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await organizer.Client.DeleteAsync(b, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    private async Task<Guid> CreateAsync(Actor organizer, DateTimeOffset when, string visibility = "Open")
    {
        var created = await Read(await organizer.Client.PostAsJsonAsync("/api/v1/matches", new
        {
            name = "Racha",
            address = "Rua Teste, 123",
            latitude = -23.561,
            longitude = -46.656,
            dateTime = when,
            maxPlayers = 12,
            regularSlots = 12,
            type = "OneOff",
            visibility,
            inviteMode = visibility == "Private" ? "Code" : null,
        }, Ct), HttpStatusCode.Created);
        return created.GetProperty("id").GetGuid();
    }

    private static object Edit(string name, DateTimeOffset when, int maxPlayers = 12) => new
    {
        name,
        address = "Rua Nova, 456",
        latitude = -23.562,
        longitude = -46.657,
        dateTime = when,
        maxPlayers,
        regularSlots = maxPlayers,
    };

    private static async Task Confirm(Actor who, Guid matchId) =>
        await Read(await who.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct));

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(expected, text);
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
