using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quadra.Api.Events;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Persistence;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;

namespace Quadra.IntegrationTests.InGame;

/// <summary>
/// A match without fixed teams (DECISIONS #86) with a stats sheet (DECISIONS #84, #85), over
/// HTTP against a real Postgres: the organizer starts and ends it with no teams and no score,
/// records the players' actions, MVP voting and the summary work, and the player's profile and
/// history get the actions with no win or loss. Strangers and players cannot write the sheet.
/// </summary>
public sealed class FreeTeamsAndStatsFlowTests : IClassFixture<InGameWebApplicationFactory>
{
    private readonly InGameWebApplicationFactory _fx;

    public FreeTeamsAndStatsFlowTests(InGameWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_free_match_with_a_stats_sheet_from_start_to_profile()
    {
        await ResetAsync();
        var organizer = Guid.NewGuid();
        var players = Enumerable.Range(0, 3).Select(_ => Guid.NewGuid()).ToArray();
        var late = Guid.NewGuid();
        var stranger = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var api = _fx.CreateAuthenticatedClient(organizer);

        var matchId = (await SendAsync(api, HttpMethod.Post, "/api/v1/matches", new
        {
            name = "Racha livre",
            address = "Arena Central",
            latitude = -23.55,
            longitude = -46.63,
            dateTime = DateTimeOffset.UtcNow.AddMinutes(10),
            maxPlayers = 12,
            regularSlots = 12,
            type = "OneOff",
            format = "4X4",
            durationMinutes = 120,
            freeTeams = true,
            confirmationOpensHoursBefore = 24,
        }, HttpStatusCode.Created)).GetProperty("id").GetGuid();
        var base_ = $"/api/v1/matches/{matchId}";
        foreach (var player in players.Append(organizer))
        {
            await ConfirmAsync(matchId, player);
        }

        (await DetailAsync(api, matchId)).GetProperty("match").GetProperty("freeTeams").GetBoolean().Should().BeTrue();

        // No sheet before the game starts.
        var sheet = new { players = new[] { Actions(players[0], kills: 4, digs: 2) } };
        await SendAsync(api, HttpMethod.Put, $"{base_}/player-stats", sheet, HttpStatusCode.Conflict);

        // ── start with no teams; there is no score to keep ──
        var created = await SendAsync(api, HttpMethod.Post, $"{base_}/scoreboard", new { format = "Free" }, HttpStatusCode.Created);
        created.GetProperty("teamAId").GetGuid().Should().Be(created.GetProperty("teamBId").GetGuid());
        var started = await SendAsync(api, HttpMethod.Post, $"{base_}/scoreboard/start", null, HttpStatusCode.OK);
        started.GetProperty("sets").GetArrayLength().Should().Be(0);
        await SendAsync(api, HttpMethod.Post, $"{base_}/scoreboard/sets/1/points",
            new { teamId = created.GetProperty("teamAId").GetGuid() }, HttpStatusCode.Conflict);

        // Someone confirms while the session runs: they played too.
        await ConfirmAsync(matchId, late);

        // ── the sheet: only the organizer writes it, only for who played ──
        var fullSheet = new { players = new[] { Actions(players[0], kills: 20, digs: 2), Actions(late, assists: 5) } };
        await SendAsync(_fx.CreateAuthenticatedClient(players[1]), HttpMethod.Put, $"{base_}/player-stats", fullSheet, HttpStatusCode.Forbidden);
        await SendAsync(stranger, HttpMethod.Put, $"{base_}/player-stats", fullSheet, HttpStatusCode.Forbidden);
        await SendAsync(api, HttpMethod.Put, $"{base_}/player-stats",
            new { players = new[] { Actions(Guid.NewGuid(), kills: 1) } }, (HttpStatusCode)422);
        await SendAsync(api, HttpMethod.Put, $"{base_}/player-stats",
            new { players = new[] { Actions(players[0], kills: 100) } }, HttpStatusCode.BadRequest);
        await SendAsync(api, HttpMethod.Put, $"{base_}/player-stats", fullSheet, HttpStatusCode.OK);

        var read = await SendAsync(_fx.CreateAuthenticatedClient(players[2]), HttpMethod.Get, $"{base_}/player-stats", null, HttpStatusCode.OK);
        read.GetProperty("editable").GetBoolean().Should().BeTrue();
        read.GetProperty("players").GetArrayLength().Should().Be(2);
        await SendAsync(stranger, HttpMethod.Get, $"{base_}/player-stats", null, HttpStatusCode.Forbidden);

        // ── end, then MVP voting (opened by the MatchEnded handler in the app; the events are
        //    a substitute here): the late player can vote ──
        var ended = await SendAsync(api, HttpMethod.Post, $"{base_}/scoreboard/end", null, HttpStatusCode.OK);
        ended.GetProperty("state").GetString().Should().Be("Ended");
        ended.GetProperty("winnerTeamId").ValueKind.Should().Be(JsonValueKind.Null);
        await SendAsync(api, HttpMethod.Post, $"{base_}/mvp-voting", new { }, HttpStatusCode.Created);
        await SendAsync(_fx.CreateAuthenticatedClient(late), HttpMethod.Post, $"{base_}/mvp-voting/votes",
            new { votedPlayerId = players[0] }, HttpStatusCode.OK);
        await SendAsync(api, HttpMethod.Post, $"{base_}/mvp-voting/close", null, HttpStatusCode.OK);

        var summary = await SendAsync(api, HttpMethod.Post, $"{base_}/summary", null, HttpStatusCode.Created);
        summary.GetProperty("format").GetString().Should().Be("Free");
        summary.GetProperty("teams").EnumerateArray().Should().ContainSingle()
            .Which.GetProperty("playerIds").EnumerateArray().Select(p => p.GetGuid())
            .Should().BeEquivalentTo(players.Append(organizer).Append(late));

        // The finished match is fixed: the sheet no longer changes.
        await SendAsync(api, HttpMethod.Put, $"{base_}/player-stats", fullSheet, HttpStatusCode.Conflict);
        (await SendAsync(api, HttpMethod.Get, $"{base_}/player-stats", null, HttpStatusCode.OK))
            .GetProperty("editable").GetBoolean().Should().BeFalse();

        // ── the profile: one match, no result, the actions; blocks were never recorded ──
        // Events are a substitute in this factory: deliver the summary to the real profile consumer.
        var generated = _fx.Publisher.ReceivedCalls()
            .SelectMany(c => c.GetArguments())
            .OfType<MatchSummaryGenerated>()
            .Single(e => e.MatchId == matchId);
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetServices<IEventHandler<MatchSummaryGenerated>>()
                .OfType<MatchSummaryGeneratedProfileConsumer>()
                .Single()
                .HandleAsync(generated, Ct);
        }

        var profile = await SendAsync(api, HttpMethod.Get, $"/api/v1/profiles/{players[0]}", null, HttpStatusCode.OK);
        var stats = profile.GetProperty("stats");
        stats.GetProperty("matchesPlayed").GetInt32().Should().Be(1);
        stats.GetProperty("wins").GetInt32().Should().Be(0);
        stats.GetProperty("draws").GetInt32().Should().Be(0);
        var actions = profile.GetProperty("actions");
        actions.GetProperty("matches").GetInt32().Should().Be(1);
        actions.GetProperty("kills").GetProperty("total").GetInt32().Should().Be(20);
        actions.GetProperty("blocks").GetProperty("perMatch").ValueKind.Should().Be(JsonValueKind.Null);
        profile.GetProperty("skills").GetProperty("attack").GetInt32().Should().BeGreaterThan(50, "20 cortadas in 2h of 4x4 (2.2 a standard game) beat a fresh rating");

        var history = await SendAsync(
            _fx.CreateAuthenticatedClient(players[0]), HttpMethod.Get, "/api/v1/profiles/me/match-history", null, HttpStatusCode.OK);
        var entry = history.GetProperty("items")[0];
        entry.GetProperty("outcome").GetString().Should().Be("NoResult");
        entry.GetProperty("actions").GetProperty("kills").GetInt32().Should().Be(20);
        entry.GetProperty("actions").GetProperty("blocks").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static object Actions(Guid playerId, int kills = 0, int blocks = 0, int aces = 0, int digs = 0, int assists = 0) =>
        new { playerId, kills, blocks, aces, digs, assists };

    private async Task ConfirmAsync(Guid matchId, Guid playerId) =>
        (await _fx.CreateAuthenticatedClient(playerId).PutAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

    private static async Task<JsonElement> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        object? body,
        HttpStatusCode expected = HttpStatusCode.OK)
    {
        var request = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        var response = await client.SendAsync(request, Ct);
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(expected, text);
        return text.Length == 0 ? default : JsonDocument.Parse(text).RootElement.Clone();
    }

    private static async Task<JsonElement> DetailAsync(HttpClient client, Guid matchId) =>
        await SendAsync(client, HttpMethod.Get, $"/api/v1/matches/{matchId}/detail", null, HttpStatusCode.OK);

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<InGameDbContext>().Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE player_match_stats, mvp_votes, mvp_votings, scoreboard_sets, scoreboards, team_members, teams RESTART IDENTITY CASCADE;", Ct);
        await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE match_guests, waiting_list, match_presences, matches RESTART IDENTITY CASCADE;", Ct);
    }
}
