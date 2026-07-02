using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quadra.Modules.InGame.Persistence;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.InGame;
using Quadra.Shared.Realtime;

namespace Quadra.IntegrationTests.InGame;

/// <summary>
/// HTTP integration tests for the F1.4 live scoreboard endpoints, backed by a real Postgres.
///
/// Coverage per acceptance criterion:
///
/// AC-1 "set-by-set scoreboard (best of 3 or 5)":
///   - POST /scoreboard with Format=BestOf3 creates a scoreboard (201).
///   - GET /scoreboard returns per-set rows.
///   - Recording points auto-finishes a set at 25 win-by-2.
///   - The game ends when a team reaches 2 sets won (BestOf3).
///
/// AC-2 "real-time point/set recording":
///   - A successful start, point, and end each invoke IMatchRoomNotifier.NotifyScoreboardUpdatedAsync.
///
/// AC-3 "match state (NotStarted | InProgress | Ended)":
///   - GET returns NotStarted after create, InProgress after start, Ended after forced end / final set.
///   - Invalid transitions (start twice, points before start) return 409.
///
/// Error paths: 401 (no JWT), 403 (non-organizer), 404 (match/scoreboard/team not found),
///              409 (duplicate scoreboard, wrong state, non-current set), 400 (invalid format).
/// Deterministic team A/B assignment: teamAId corresponds to the team named "Team A".
/// </summary>
public sealed class ScoreboardEndpointsTests : IClassFixture<InGameWebApplicationFactory>
{
    private readonly InGameWebApplicationFactory _fx;
    private static readonly Guid OrganizerUserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    public ScoreboardEndpointsTests(InGameWebApplicationFactory fx)
    {
        _fx = fx;
    }

    // ─── Reset / setup helpers ───────────────────────────────────────────────

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var matchesCtx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var inGameCtx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();

        await inGameCtx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE scoreboard_sets, scoreboards, team_members, teams RESTART IDENTITY CASCADE;");
        await matchesCtx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE waiting_list, match_presences, matches RESTART IDENTITY CASCADE;");

        _fx.Publisher.ClearReceivedCalls();
        _fx.Notifier.ClearReceivedCalls();
    }

    private async Task<Guid> CreateMatchAsync(Guid organizerId)
    {
        var client = _fx.CreateAuthenticatedClient(organizerId);
        var response = await client.PostAsJsonAsync("/api/v1/matches", BuildMatchPayload(), TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    private async Task AddConfirmedPresenceAsync(Guid matchId, Guid playerId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var now = DateTimeOffset.UtcNow;
        var presence = MatchPresence.Create(matchId, playerId, PlayerType.Regular, now);
        presence.Confirm(now);
        ctx.Presences.Add(presence);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task ForceMatchClosedAsync(Guid matchId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await ctx.Database.ExecuteSqlAsync(
            $"UPDATE matches SET status = 'Closed' WHERE id = {matchId}",
            TestContext.Current.CancellationToken);
    }

    /// <summary>Creates a match, adds confirmed players, closes it and drafts the two teams.</summary>
    private async Task<Guid> CreateClosedMatchWithTeamsAsync(Guid organizerId, int players = 4)
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(organizerId);
        for (var i = 0; i < players; i++)
        {
            await AddConfirmedPresenceAsync(matchId, Guid.NewGuid());
        }
        await ForceMatchClosedAsync(matchId);

        var client = _fx.CreateAuthenticatedClient(organizerId);
        var draft = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft", null, TestContext.Current.CancellationToken);
        draft.StatusCode.Should().Be(HttpStatusCode.OK);
        return matchId;
    }

    private async Task<HttpResponseMessage> CreateScoreboardAsync(HttpClient client, Guid matchId, string format) =>
        await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/scoreboard",
            new { Format = format },
            TestContext.Current.CancellationToken);

    private async Task<JsonElement> GetScoreboardAsync(HttpClient client, Guid matchId)
    {
        var response = await client.GetAsync(
            $"/api/v1/matches/{matchId}/scoreboard", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    /// <summary>Records <paramref name="count"/> points for <paramref name="teamId"/> on the current set.</summary>
    private async Task<JsonElement> RecordPointsAsync(
        HttpClient client, Guid matchId, int setNumber, Guid teamId, int count)
    {
        JsonElement last = default;
        for (var i = 0; i < count; i++)
        {
            var response = await client.PostAsJsonAsync(
                $"/api/v1/matches/{matchId}/scoreboard/sets/{setNumber}/points",
                new { TeamId = teamId },
                TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            last = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        }
        return last;
    }

    // ─── POST /scoreboard — 401 unauthenticated ──────────────────────────────

    /// <summary>Covers: F1.4 — 401 when no JWT is provided for POST /scoreboard.</summary>
    [Fact]
    public async Task POST_scoreboard_without_JWT_returns_401()
    {
        var client = _fx.CreateAnonymousClient();
        var response = await CreateScoreboardAsync(client, Guid.NewGuid(), "BestOf3");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── POST /scoreboard — 400 invalid format ───────────────────────────────

    /// <summary>Covers: F1.4 — 400 when the Format is invalid.</summary>
    [Fact]
    public async Task POST_scoreboard_with_invalid_format_returns_400()
    {
        var matchId = await CreateClosedMatchWithTeamsAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var response = await CreateScoreboardAsync(client, matchId, "BestOf7");
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── POST /scoreboard — 404 match not found ──────────────────────────────

    /// <summary>Covers: F1.4 — 404 when the match does not exist.</summary>
    [Fact]
    public async Task POST_scoreboard_with_nonexistent_match_returns_404()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await CreateScoreboardAsync(client, Guid.NewGuid(), "BestOf3");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── POST /scoreboard — 403 non-organizer ────────────────────────────────

    /// <summary>Covers: F1.4 — 403 when the caller is not the organizer.</summary>
    [Fact]
    public async Task POST_scoreboard_by_non_organizer_returns_403()
    {
        var matchId = await CreateClosedMatchWithTeamsAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OtherUserId);

        var response = await CreateScoreboardAsync(client, matchId, "BestOf3");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── POST /scoreboard — 409 match not Closed ─────────────────────────────

    /// <summary>Covers: F1.4 — 409 when the match status is not Closed.</summary>
    [Fact]
    public async Task POST_scoreboard_when_match_not_Closed_returns_409()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId); // Draft status
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var response = await CreateScoreboardAsync(client, matchId, "BestOf3");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── POST /scoreboard — 404 teams not formed ─────────────────────────────

    /// <summary>Covers: F1.4 — 404 when teams have not been formed for the match.</summary>
    [Fact]
    public async Task POST_scoreboard_when_teams_not_formed_returns_404()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId); // Closed but no draft performed
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var response = await CreateScoreboardAsync(client, matchId, "BestOf3");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── POST /scoreboard — 201 created + persistence + deterministic teams ──

    /// <summary>
    /// Covers: F1.4 AC-1 + AC-3 — POST /scoreboard creates a scoreboard (201) in NotStarted state,
    /// persists it, and assigns teamAId/teamBId deterministically (teamAId = the "Team A" team).
    /// </summary>
    [Fact]
    public async Task POST_scoreboard_with_valid_data_returns_201_NotStarted_and_persists()
    {
        var matchId = await CreateClosedMatchWithTeamsAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var response = await CreateScoreboardAsync(client, matchId, "BestOf3");
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("state").GetString().Should().Be("NotStarted");
        body.GetProperty("format").GetString().Should().Be("BestOf3");
        body.GetProperty("currentSetNumber").GetInt32().Should().Be(0);
        body.GetProperty("sets").GetArrayLength().Should().Be(0);

        // Deterministic assignment: teamAId corresponds to the team named "Team A".
        var teamsResponse = await client.GetAsync(
            $"/api/v1/matches/{matchId}/teams", TestContext.Current.CancellationToken);
        var teamsBody = await teamsResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var teams = teamsBody.GetProperty("teams");
        var teamAId = Enumerable.Range(0, teams.GetArrayLength())
            .Select(i => teams[i])
            .First(t => t.GetProperty("name").GetString() == "Team A")
            .GetProperty("id").GetGuid();
        body.GetProperty("teamAId").GetGuid().Should().Be(teamAId);

        // Persistence check.
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();
        (await ctx.Scoreboards.CountAsync(
            s => s.MatchId == matchId, TestContext.Current.CancellationToken)).Should().Be(1);
    }

    // ─── POST /scoreboard — 409 duplicate ────────────────────────────────────

    /// <summary>Covers: F1.4 — 409 when a scoreboard already exists for the match.</summary>
    [Fact]
    public async Task POST_scoreboard_twice_returns_409()
    {
        var matchId = await CreateClosedMatchWithTeamsAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        (await CreateScoreboardAsync(client, matchId, "BestOf3")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CreateScoreboardAsync(client, matchId, "BestOf3")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── GET /scoreboard — 404 no scoreboard ─────────────────────────────────

    /// <summary>Covers: F1.4 — 404 when no scoreboard exists yet for the match.</summary>
    [Fact]
    public async Task GET_scoreboard_when_not_created_returns_404()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var response = await client.GetAsync(
            $"/api/v1/matches/{matchId}/scoreboard", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── POST /start — 409 not created, 200 success, 409 twice ───────────────

    /// <summary>Covers: F1.4 — 404 when starting a match with no scoreboard.</summary>
    [Fact]
    public async Task POST_start_without_scoreboard_returns_404()
    {
        var matchId = await CreateClosedMatchWithTeamsAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var response = await client.PostAsync(
            $"/api/v1/matches/{matchId}/scoreboard/start", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers: F1.4 AC-3 — POST /start transitions the scoreboard to InProgress and opens set 1;
    /// AC-2 — a successful start broadcasts NotifyScoreboardUpdatedAsync;
    /// also publishes MatchStarted.
    /// </summary>
    [Fact]
    public async Task POST_start_transitions_to_InProgress_and_broadcasts()
    {
        var matchId = await CreateClosedMatchWithTeamsAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        await CreateScoreboardAsync(client, matchId, "BestOf3");

        var response = await client.PostAsync(
            $"/api/v1/matches/{matchId}/scoreboard/start", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterStart = await GetScoreboardAsync(client, matchId);
        afterStart.GetProperty("state").GetString().Should().Be("InProgress");
        afterStart.GetProperty("currentSetNumber").GetInt32().Should().Be(1);
        afterStart.GetProperty("sets").GetArrayLength().Should().Be(1);

        await _fx.Notifier.Received().NotifyScoreboardUpdatedAsync(
            Arg.Is<ScoreboardUpdatedMessage>(m => m.MatchId == matchId && m.State == "InProgress"),
            Arg.Any<CancellationToken>());
        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<MatchStarted>(e => e.MatchId == matchId), Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: F1.4 AC-3 — starting an already-started game returns 409 (invalid transition).</summary>
    [Fact]
    public async Task POST_start_twice_returns_409()
    {
        var matchId = await CreateClosedMatchWithTeamsAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        await CreateScoreboardAsync(client, matchId, "BestOf3");

        (await client.PostAsync($"/api/v1/matches/{matchId}/scoreboard/start", null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/matches/{matchId}/scoreboard/start", null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── POST /points — 409 before start ─────────────────────────────────────

    /// <summary>Covers: F1.4 AC-3 — recording a point before the game starts returns 409.</summary>
    [Fact]
    public async Task POST_points_before_start_returns_409()
    {
        var matchId = await CreateClosedMatchWithTeamsAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var created = await CreateScoreboardAsync(client, matchId, "BestOf3");
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var teamAId = body.GetProperty("teamAId").GetGuid();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/scoreboard/sets/1/points",
            new { TeamId = teamAId },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── POST /points — 400 empty team, 404 unknown team, 409 non-current ────

    /// <summary>Covers: F1.4 — 400 when TeamId is empty.</summary>
    [Fact]
    public async Task POST_points_with_empty_teamId_returns_400()
    {
        var (matchId, client, _) = await StartScoreboardAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/scoreboard/sets/1/points",
            new { TeamId = Guid.Empty },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers: F1.4 — 404 when the scoring team is not part of the scoreboard.</summary>
    [Fact]
    public async Task POST_points_with_unknown_team_returns_404()
    {
        var (matchId, client, _) = await StartScoreboardAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/scoreboard/sets/1/points",
            new { TeamId = Guid.NewGuid() },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Covers: F1.4 — 409 when recording a point for a set that is not the current set.</summary>
    [Fact]
    public async Task POST_points_for_non_current_set_returns_409()
    {
        var (matchId, client, teamAId) = await StartScoreboardAsync();

        var response = await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/scoreboard/sets/2/points", // current set is 1
            new { TeamId = teamAId },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── AC-1 + AC-2: point recording, set auto-finish, per-set rows, broadcast

    /// <summary>
    /// Covers: F1.4 AC-1 — recording 25 points auto-finishes set 1 (win-by-2 at 25-0), advances to set 2,
    /// and GET exposes both per-set rows; AC-2 — each point broadcasts NotifyScoreboardUpdatedAsync.
    /// </summary>
    [Fact]
    public async Task POST_points_auto_finishes_set_at_25_and_exposes_per_set_rows()
    {
        var (matchId, client, teamAId) = await StartScoreboardAsync();
        _fx.Notifier.ClearReceivedCalls(); // ignore the start broadcast; count only point broadcasts

        await RecordPointsAsync(client, matchId, setNumber: 1, teamAId, 25);

        var board = await GetScoreboardAsync(client, matchId);
        board.GetProperty("state").GetString().Should().Be("InProgress");
        board.GetProperty("teamASetsWon").GetInt32().Should().Be(1);
        board.GetProperty("currentSetNumber").GetInt32().Should().Be(2);

        var sets = board.GetProperty("sets");
        sets.GetArrayLength().Should().Be(2);
        var set1 = sets[0];
        set1.GetProperty("setNumber").GetInt32().Should().Be(1);
        set1.GetProperty("status").GetString().Should().Be("Finished");
        set1.GetProperty("teamAPoints").GetInt32().Should().Be(25);
        set1.GetProperty("winnerTeamId").GetGuid().Should().Be(teamAId);

        await _fx.Notifier.Received(25).NotifyScoreboardUpdatedAsync(
            Arg.Any<ScoreboardUpdatedMessage>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.4 AC-1 + AC-3 — a full BestOf3 game: winning two sets ends the game;
    /// GET returns Ended with the correct winner; MatchEnded is published.
    /// </summary>
    [Fact]
    public async Task Full_BestOf3_game_ends_after_two_sets_and_publishes_MatchEnded()
    {
        var (matchId, client, teamAId) = await StartScoreboardAsync();

        await RecordPointsAsync(client, matchId, setNumber: 1, teamAId, 25); // set 1 → A
        await RecordPointsAsync(client, matchId, setNumber: 2, teamAId, 25); // set 2 → A → game over

        var board = await GetScoreboardAsync(client, matchId);
        board.GetProperty("state").GetString().Should().Be("Ended");
        board.GetProperty("teamASetsWon").GetInt32().Should().Be(2);
        board.GetProperty("winnerTeamId").GetGuid().Should().Be(teamAId);
        board.GetProperty("endedAt").ValueKind.Should().NotBe(JsonValueKind.Null);

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<MatchEnded>(e => e.MatchId == matchId && e.WinnerTeamId == teamAId),
            Arg.Any<CancellationToken>());
    }

    // ─── POST /end — 200 forced end + broadcast + 409 when not in progress ───

    /// <summary>
    /// Covers: F1.4 AC-3 — POST /end force-ends an in-progress game (state → Ended);
    /// AC-2 — the end broadcasts NotifyScoreboardUpdatedAsync; MatchEnded is published.
    /// </summary>
    [Fact]
    public async Task POST_end_forces_Ended_and_broadcasts()
    {
        var (matchId, client, _) = await StartScoreboardAsync();
        _fx.Notifier.ClearReceivedCalls();

        var response = await client.PostAsync(
            $"/api/v1/matches/{matchId}/scoreboard/end", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var board = await GetScoreboardAsync(client, matchId);
        board.GetProperty("state").GetString().Should().Be("Ended");

        await _fx.Notifier.Received().NotifyScoreboardUpdatedAsync(
            Arg.Is<ScoreboardUpdatedMessage>(m => m.MatchId == matchId && m.State == "Ended"),
            Arg.Any<CancellationToken>());
        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<MatchEnded>(e => e.MatchId == matchId), Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: F1.4 AC-3 — ending a not-started game returns 409.</summary>
    [Fact]
    public async Task POST_end_before_start_returns_409()
    {
        var matchId = await CreateClosedMatchWithTeamsAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        await CreateScoreboardAsync(client, matchId, "BestOf3");

        var response = await client.PostAsync(
            $"/api/v1/matches/{matchId}/scoreboard/end", null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── Shared setup for started scoreboards ────────────────────────────────

    /// <summary>Creates a closed match with teams, a BestOf3 scoreboard, and starts it (set 1 open).</summary>
    private async Task<StartedScoreboard> StartScoreboardAsync()
    {
        var matchId = await CreateClosedMatchWithTeamsAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var created = await CreateScoreboardAsync(client, matchId, "BestOf3");
        var body = await created.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var teamAId = body.GetProperty("teamAId").GetGuid();
        (await client.PostAsync($"/api/v1/matches/{matchId}/scoreboard/start", null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        return new StartedScoreboard(matchId, client, teamAId);
    }

    private sealed record StartedScoreboard(Guid MatchId, HttpClient Client, Guid TeamAId);

    // ─── Match payload helper ────────────────────────────────────────────────

    private static MatchRequestPayload BuildMatchPayload() =>
        new(
            Name: "Test Match",
            Description: null,
            Address: "Rua Teste, 123, São Paulo",
            Latitude: -23.5505,
            Longitude: -46.6333,
            DateTime: DateTimeOffset.UtcNow.AddDays(30),
            MaxPlayers: 12,
            RegularSlots: 8,
            Price: null,
            Type: "OneOff",
            Frequency: null,
            DayOfWeek: null,
            WindowOpensAt: DateTimeOffset.UtcNow.AddDays(5),
            WindowClosesAt: DateTimeOffset.UtcNow.AddDays(15));

    private sealed record MatchRequestPayload(
        string Name,
        string? Description,
        string Address,
        double Latitude,
        double Longitude,
        DateTimeOffset DateTime,
        int MaxPlayers,
        int RegularSlots,
        decimal? Price,
        string Type,
        string? Frequency,
        int? DayOfWeek,
        DateTimeOffset WindowOpensAt,
        DateTimeOffset WindowClosesAt);
}
