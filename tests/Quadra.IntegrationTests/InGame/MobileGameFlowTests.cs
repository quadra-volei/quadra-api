using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quadra.Modules.InGame.Persistence;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Realtime;

namespace Quadra.IntegrationTests.InGame;

/// <summary>
/// The whole game of a pickup match as the mobile app drives it, over HTTP against a real
/// Postgres: players join an open match, the organizer adds a guest and draws three teams,
/// sets are played by pairs the organizer picks (winner stays), a point is undone, a set is
/// ended early, MVP is voted, the summary is generated — and the match detail says where the
/// game is at each step. Also: the SignalR hub is reachable only with a token.
/// </summary>
public sealed class MobileGameFlowTests : IClassFixture<InGameWebApplicationFactory>
{
    private readonly InGameWebApplicationFactory _fx;

    public MobileGameFlowTests(InGameWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_three_team_game_from_the_draw_to_the_summary()
    {
        await ResetAsync();
        var organizer = Guid.NewGuid();
        var players = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        var api = _fx.CreateAuthenticatedClient(organizer);

        // ── an open match with 5 players + the organizer + 1 guest ──
        var matchId = (await PostAsync(api, "/api/v1/matches", new
        {
            name = "Racha de Quinta",
            address = "Arena Central",
            latitude = -23.55,
            longitude = -46.63,
            dateTime = DateTimeOffset.UtcNow.AddHours(2),
            maxPlayers = 12,
            regularSlots = 12,
            type = "OneOff",
            confirmationOpensHoursBefore = 24,
        }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        foreach (var player in players.Append(organizer))
        {
            (await _fx.CreateAuthenticatedClient(player).PutAsJsonAsync(
                $"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        var guestId = (await PostAsync(api, $"/api/v1/matches/{matchId}/guests", new { name = "Zé" }, HttpStatusCode.Created))
            .GetProperty("id").GetGuid();
        (await DetailAsync(api, matchId)).GetProperty("game").ValueKind.Should().Be(JsonValueKind.Null);

        // ── draw three teams while confirmations are still open ──
        var base_ = $"/api/v1/matches/{matchId}";
        (await api.PostAsJsonAsync($"{base_}/teams/draft", new { teamCount = 5 }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var teams = (await PostAsync(api, $"{base_}/teams/draft", new { teamCount = 3, mode = "Balanced" }, HttpStatusCode.OK))
            .GetProperty("teams").EnumerateArray().ToList();
        teams.Should().HaveCount(3);
        var members = teams.SelectMany(t => t.GetProperty("members").EnumerateArray()).ToList();
        members.Should().HaveCount(7);
        members.Single(m => m.GetProperty("isGuest").GetBoolean()).GetProperty("playerId").GetGuid().Should().Be(guestId);
        var (teamA, teamB, teamC) = (Id(teams[0]), Id(teams[1]), Id(teams[2]));

        // ── first set: A x C, picked by the organizer ──
        (await api.PostAsJsonAsync($"{base_}/scoreboard", new { format = "BestOf3", teamAId = teamA, teamBId = teamA }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var created = await PostAsync(
            api, $"{base_}/scoreboard", new { format = "BestOf3", teamAId = teamA, teamBId = teamC }, HttpStatusCode.Created);
        created.GetProperty("rotatesTeams").GetBoolean().Should().BeTrue();
        await PostAsync(api, $"{base_}/scoreboard/start", null, HttpStatusCode.OK);
        (await DetailAsync(api, matchId)).GetProperty("game").GetProperty("scoreboardState").GetString()
            .Should().Be("InProgress");

        // A mis-tap for C is undone; players cannot undo.
        await PointsAsync(api, base_, 1, teamA, 3);
        var mistap = await PointsAsync(api, base_, 1, teamC, 1);
        mistap.GetProperty("sets")[0].GetProperty("canUndo").GetBoolean().Should().BeTrue();
        (await _fx.CreateAuthenticatedClient(players[0]).DeleteAsync($"{base_}/scoreboard/sets/1/points/last", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var undone = await SendAsync(api, HttpMethod.Delete, $"{base_}/scoreboard/sets/1/points/last");
        undone.GetProperty("sets")[0].GetProperty("teamBPoints").GetInt32().Should().Be(0);
        (await api.DeleteAsync($"{base_}/scoreboard/sets/1/points/last", Ct)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        // The organizer ends the set early: A, ahead 3–0, takes it and the game waits.
        var afterFirst = await PostAsync(api, $"{base_}/scoreboard/sets/1/end", null, HttpStatusCode.OK);
        afterFirst.GetProperty("awaitingNextSet").GetBoolean().Should().BeTrue();
        afterFirst.GetProperty("sets")[0].GetProperty("winnerTeamId").GetGuid().Should().Be(teamA);
        (await api.PostAsJsonAsync($"{base_}/scoreboard/sets/1/points", new { teamId = teamA }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        // ── second set: winner stays, B comes in; A wins again and takes the game ──
        var second = await PostAsync(api, $"{base_}/scoreboard/sets", new { teamAId = teamA, teamBId = teamB }, HttpStatusCode.OK);
        second.GetProperty("currentSetNumber").GetInt32().Should().Be(2);
        second.GetProperty("teamASetsWon").GetInt32().Should().Be(1);
        second.GetProperty("sets")[1].GetProperty("teamBId").GetGuid().Should().Be(teamB);
        var ended = await PointsAsync(api, base_, 2, teamA, 25);
        ended.GetProperty("state").GetString().Should().Be("Ended");
        ended.GetProperty("winnerTeamId").GetGuid().Should().Be(teamA);
        await _fx.Notifier.Received().NotifyScoreboardUpdatedAsync(
            Arg.Is<ScoreboardUpdatedMessage>(m => m.MatchId == matchId && m.State == "Ended"),
            Arg.Any<CancellationToken>());

        // ── MVP: a guest can be neither voter nor voted; then the summary ──
        await PostAsync(api, $"{base_}/mvp-voting", new { }, HttpStatusCode.Created);
        var voter = _fx.CreateAuthenticatedClient(players[0]);
        (await voter.PostAsJsonAsync($"{base_}/mvp-voting/votes", new { votedPlayerId = guestId }, Ct))
            .StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await voter.PostAsJsonAsync($"{base_}/mvp-voting/votes", new { votedPlayerId = players[1] }, Ct))
            .IsSuccessStatusCode.Should().BeTrue();
        await PostAsync(api, $"{base_}/mvp-voting/close", null, HttpStatusCode.OK);

        var summaryResponse = await api.PostAsync($"{base_}/summary", null, Ct);
        summaryResponse.IsSuccessStatusCode.Should().BeTrue();
        var summary = await summaryResponse.Content.ReadFromJsonAsync<JsonElement>(Ct);
        summary.GetProperty("winnerTeamId").GetGuid().Should().Be(teamA);
        summary.GetProperty("mvpPlayerId").GetGuid().Should().Be(players[1]);
        summary.GetProperty("teams").EnumerateArray()
            .SelectMany(t => t.GetProperty("playerIds").EnumerateArray())
            .Select(p => p.GetGuid())
            .Should().HaveCount(6).And.NotContain(guestId, "guests have no account to receive stats");

        var game = (await DetailAsync(api, matchId)).GetProperty("game");
        game.GetProperty("scoreboardState").GetString().Should().Be("Ended");
        game.GetProperty("mvpVotingState").GetString().Should().Be("Closed");
        game.GetProperty("hasSummary").GetBoolean().Should().BeTrue();
    }

    /// <summary>Covers: the live-updates hub exists and requires a token.</summary>
    [Fact]
    public async Task The_match_hub_negotiates_only_with_a_token()
    {
        var anonymous = await _fx.CreateAnonymousClient().PostAsync("/hubs/match/negotiate?negotiateVersion=1", null, Ct);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var authenticated = await _fx.CreateAuthenticatedClient(Guid.NewGuid())
            .PostAsync("/hubs/match/negotiate?negotiateVersion=1", null, Ct);
        authenticated.StatusCode.Should().Be(HttpStatusCode.OK);
        (await authenticated.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("availableTransports").EnumerateArray()
            .Select(t => t.GetProperty("transport").GetString())
            .Should().Contain("WebSockets");
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static Guid Id(JsonElement element) => element.GetProperty("id").GetGuid();

    private static async Task<JsonElement> PostAsync(HttpClient client, string url, object? body, HttpStatusCode expected)
    {
        var response = body is null
            ? await client.PostAsync(url, null, Ct)
            : await client.PostAsJsonAsync(url, body, Ct);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> SendAsync(HttpClient client, HttpMethod method, string url)
    {
        var response = await client.SendAsync(new HttpRequestMessage(method, url), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync(Ct));
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<JsonElement> PointsAsync(HttpClient client, string matchUrl, int setNumber, Guid teamId, int count)
    {
        JsonElement last = default;
        for (var i = 0; i < count; i++)
        {
            last = await PostAsync(
                client, $"{matchUrl}/scoreboard/sets/{setNumber}/points", new { teamId }, HttpStatusCode.OK);
        }

        return last;
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid matchId)
    {
        var response = await client.GetAsync($"/api/v1/matches/{matchId}/detail", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InGameDbContext>().Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE mvp_votes, mvp_votings, scoreboard_sets, scoreboards, team_members, teams RESTART IDENTITY CASCADE;", Ct);
        await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE match_guests, waiting_list, match_presences, matches RESTART IDENTITY CASCADE;", Ct);
        _fx.Notifier.ClearReceivedCalls();
    }
}
