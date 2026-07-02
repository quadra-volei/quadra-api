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
using Quadra.Shared.Events.Matches;

namespace Quadra.IntegrationTests.InGame;

/// <summary>
/// HTTP integration tests for the F1.6 match-summary endpoints, backed by a real Postgres and the
/// full InGame → Matches pipeline (scoreboard, teams, MVP voting feeding the immutable summary).
///
/// Coverage per acceptance criterion:
///
/// AC "endpoint returning final score, duration, MVP, team composition":
///   - after a scoreboard reaches Ended, the organizer POSTs the summary (201) and GET returns 200
///     with per-set final score + sets-won, DurationSeconds, MVP fields and each team's PlayerIds;
///   - POST transitions matches.status to Ended and publishes MatchSummaryGenerated + MatchStatusChanged.
///
/// AC "immutable record (cannot be edited afterwards)":
///   - a second POST for a match that already has a summary returns 409;
///   - PUT / PATCH / DELETE on the summary resource do not exist (405);
///   - the persisted row is unchanged after the second POST attempt.
///
/// Error paths from the POST behavior steps: 401 (no JWT), 403 (non-organizer),
/// 404 (match not found / no scoreboard / no summary on GET),
/// 409 (scoreboard not Ended / MVP voting Open / cancelled match).
/// </summary>
public sealed class MatchSummaryEndpointsTests : IClassFixture<InGameWebApplicationFactory>
{
    private readonly InGameWebApplicationFactory _fx;

    private static readonly Guid OrganizerUserId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Outsider = new("99999999-9999-9999-9999-999999999999");

    private static readonly Guid P1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid P2 = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid P3 = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid P4 = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid[] Participants = [P1, P2, P3, P4];

    public MatchSummaryEndpointsTests(InGameWebApplicationFactory fx)
    {
        _fx = fx;
    }

    // ═══ AC: 201 + GET 200 with full content ══════════════════════════════════

    /// <summary>
    /// Covers: F1.6 AC "endpoint returning final score, duration, MVP, team composition" — after the
    /// scoreboard is Ended and MVP voting is Closed, the organizer POSTs (201), the summary persists,
    /// matches.status becomes Ended, and GET returns the per-set score, sets-won, duration, MVP and teams.
    /// </summary>
    [Fact]
    public async Task POST_then_GET_returns_full_summary_and_marks_match_ended()
    {
        var ctx = await SetupSummarizableMatchAsync();

        var organizer = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var post = await organizer.PostAsync(SummaryUrl(ctx.MatchId), null, TestContext.Current.CancellationToken);
        post.StatusCode.Should().Be(HttpStatusCode.Created);
        post.Headers.Location.Should().NotBeNull();

        var created = await post.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        created.GetProperty("id").GetGuid().Should().NotBeEmpty();

        // GET returns the immutable summary.
        var get = await organizer.GetAsync(SummaryUrl(ctx.MatchId), TestContext.Current.CancellationToken);
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        body.GetProperty("matchId").GetGuid().Should().Be(ctx.MatchId);
        body.GetProperty("format").GetString().Should().Be("BestOf3");

        // Final score: Team A won both sets 25-0 → 2-0, winner = Team A.
        body.GetProperty("teamASetsWon").GetInt32().Should().Be(2);
        body.GetProperty("teamBSetsWon").GetInt32().Should().Be(0);
        body.GetProperty("winnerTeamId").GetGuid().Should().Be(ctx.TeamAId);

        var sets = body.GetProperty("sets");
        sets.GetArrayLength().Should().Be(2);
        sets[0].GetProperty("setNumber").GetInt32().Should().Be(1);
        sets[0].GetProperty("teamAPoints").GetInt32().Should().Be(25);
        sets[0].GetProperty("teamBPoints").GetInt32().Should().Be(0);
        sets[0].GetProperty("winnerTeamId").GetGuid().Should().Be(ctx.TeamAId);

        // Duration is a non-negative precomputed integer.
        body.GetProperty("durationSeconds").GetInt32().Should().BeGreaterThanOrEqualTo(0);

        // MVP: P2 received 3 of 4 votes.
        body.GetProperty("mvpPlayerId").GetGuid().Should().Be(P2);
        body.GetProperty("mvpVoteCount").GetInt32().Should().Be(3);
        body.GetProperty("mvpTotalVotes").GetInt32().Should().Be(4);

        // Team composition: two teams, all four participants present exactly once.
        var teams = body.GetProperty("teams");
        teams.GetArrayLength().Should().Be(2);
        var allPlayers = Enumerable.Range(0, teams.GetArrayLength())
            .SelectMany(i => Enumerable
                .Range(0, teams[i].GetProperty("playerIds").GetArrayLength())
                .Select(j => teams[i].GetProperty("playerIds")[j].GetGuid()))
            .ToList();
        allPlayers.Should().BeEquivalentTo(Participants);

        // matches.status transitioned to Ended.
        using var scope = _fx.Factory.Services.CreateScope();
        var matchesCtx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var status = await matchesCtx.Database
            .SqlQuery<string>($"SELECT status AS \"Value\" FROM matches WHERE id = {ctx.MatchId}")
            .SingleAsync(TestContext.Current.CancellationToken);
        status.Should().Be("Ended");

        // Events published: MatchSummaryGenerated + MatchStatusChanged (Closed → Ended).
        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<MatchSummaryGenerated>(e => e.MatchId == ctx.MatchId && e.MvpPlayerId == P2),
            Arg.Any<CancellationToken>());
        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<MatchStatusChanged>(e => e.MatchId == ctx.MatchId && e.NewStatus == "Ended"),
            Arg.Any<CancellationToken>());
    }

    // ═══ AC: immutability ═════════════════════════════════════════════════════

    /// <summary>
    /// Covers: F1.6 AC "immutable record" — a second POST for a match that already has a summary
    /// returns 409 and the persisted row is left untouched (still exactly one summary).
    /// </summary>
    [Fact]
    public async Task POST_twice_returns_409_and_does_not_duplicate_the_row()
    {
        var ctx = await SetupSummarizableMatchAsync();
        var organizer = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var first = await organizer.PostAsync(SummaryUrl(ctx.MatchId), null, TestContext.Current.CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var firstId = firstBody.GetProperty("id").GetGuid();

        var second = await organizer.PostAsync(SummaryUrl(ctx.MatchId), null, TestContext.Current.CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);

        using var scope = _fx.Factory.Services.CreateScope();
        var matchesCtx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var count = await matchesCtx.Database
            .SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM match_summaries WHERE match_id = {ctx.MatchId}")
            .SingleAsync(TestContext.Current.CancellationToken);
        count.Should().Be(1, "the immutable summary must not be regenerated");

        // The still-present row is the original one.
        var get = await organizer.GetAsync(SummaryUrl(ctx.MatchId), TestContext.Current.CancellationToken);
        var body = await get.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("id").GetGuid().Should().Be(firstId);
    }

    /// <summary>
    /// Covers: F1.6 AC "immutable record" — the summary resource exposes no PUT / PATCH / DELETE verb
    /// (only GET and POST are mapped), so mutating requests are rejected with 405 Method Not Allowed.
    /// </summary>
    [Theory]
    [InlineData("PUT")]
    [InlineData("PATCH")]
    [InlineData("DELETE")]
    public async Task Mutating_verbs_on_summary_are_not_allowed(string method)
    {
        var ctx = await SetupSummarizableMatchAsync();
        var organizer = _fx.CreateAuthenticatedClient(OrganizerUserId);
        (await organizer.PostAsync(SummaryUrl(ctx.MatchId), null, TestContext.Current.CancellationToken))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var request = new HttpRequestMessage(new HttpMethod(method), SummaryUrl(ctx.MatchId));
        var response = await organizer.SendAsync(request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
    }

    // ═══ Error paths ══════════════════════════════════════════════════════════

    /// <summary>Covers: F1.6 POST step 1 — 401 when no JWT is provided.</summary>
    [Fact]
    public async Task POST_without_JWT_returns_401()
    {
        var client = _fx.CreateAnonymousClient();
        var response = await client.PostAsync(SummaryUrl(Guid.NewGuid()), null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Covers: F1.6 GET — 401 when no JWT is provided.</summary>
    [Fact]
    public async Task GET_without_JWT_returns_401()
    {
        var client = _fx.CreateAnonymousClient();
        var response = await client.GetAsync(SummaryUrl(Guid.NewGuid()), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Covers: F1.6 POST step 3 — 403 when the caller is not the organizer.</summary>
    [Fact]
    public async Task POST_by_non_organizer_returns_403()
    {
        var ctx = await SetupSummarizableMatchAsync();
        var client = _fx.CreateAuthenticatedClient(Outsider);

        var response = await client.PostAsync(SummaryUrl(ctx.MatchId), null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Covers: F1.6 POST step 2 — 404 when the match does not exist.</summary>
    [Fact]
    public async Task POST_for_nonexistent_match_returns_404()
    {
        await ResetAsync();
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PostAsync(SummaryUrl(Guid.NewGuid()), null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Covers: F1.6 POST step 5 — 404 when no scoreboard exists for the match.</summary>
    [Fact]
    public async Task POST_when_no_scoreboard_returns_404()
    {
        var matchId = await SetupMatchWithTeamsAsync(); // teams drafted, but no scoreboard created
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var response = await client.PostAsync(SummaryUrl(matchId), null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Covers: F1.6 POST step 6 — 409 when the scoreboard has not reached Ended.</summary>
    [Fact]
    public async Task POST_when_scoreboard_not_ended_returns_409()
    {
        var matchId = await SetupMatchWithTeamsAsync();
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        // Create the scoreboard but leave it NotStarted.
        (await client.PostAsJsonAsync($"/api/v1/matches/{matchId}/scoreboard", new { Format = "BestOf3" },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await client.PostAsync(SummaryUrl(matchId), null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Covers: F1.6 POST step 7 — 409 when an MVP voting session is still Open.</summary>
    [Fact]
    public async Task POST_when_mvp_voting_open_returns_409()
    {
        var matchId = await SetupMatchWithTeamsAsync();
        var teams = await GetTeamsAsync(matchId);
        await PlayFullBestOf3Async(matchId, teams.TeamAId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        // Open MVP voting and leave it Open.
        (await client.PostAsJsonAsync($"/api/v1/matches/{matchId}/mvp-voting", new { DeadlineAt = (DateTimeOffset?)null },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await client.PostAsync(SummaryUrl(matchId), null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Covers: F1.6 POST step 10 + "cancelled matches out of scope" — 409 when the match is Cancelled,
    /// even though the scoreboard is Ended.
    /// </summary>
    [Fact]
    public async Task POST_for_cancelled_match_returns_409()
    {
        var matchId = await SetupMatchWithTeamsAsync();
        var teams = await GetTeamsAsync(matchId);
        await PlayFullBestOf3Async(matchId, teams.TeamAId);
        await ForceMatchStatusAsync(matchId, "Cancelled");

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PostAsync(SummaryUrl(matchId), null, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Covers: F1.6 GET step 2 — 404 when no summary has been generated for the match.</summary>
    [Fact]
    public async Task GET_when_no_summary_returns_404()
    {
        var matchId = await SetupMatchWithTeamsAsync();
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync(SummaryUrl(matchId), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Covers: F1.6 GET step 1 — 404 when the match does not exist.</summary>
    [Fact]
    public async Task GET_for_nonexistent_match_returns_404()
    {
        await ResetAsync();
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync(SummaryUrl(Guid.NewGuid()), TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ═══ Setup helpers ════════════════════════════════════════════════════════

    private static string SummaryUrl(Guid matchId) => $"/api/v1/matches/{matchId}/summary";

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var matchesCtx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var inGameCtx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();

        await inGameCtx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE mvp_votes, mvp_votings, scoreboard_sets, scoreboards, team_members, teams RESTART IDENTITY CASCADE;");
        await matchesCtx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE match_summary_players, match_summary_sets, match_summaries, waiting_list, match_presences, matches RESTART IDENTITY CASCADE;");

        _fx.Publisher.ClearReceivedCalls();
        _fx.Notifier.ClearReceivedCalls();
    }

    private async Task<Guid> CreateMatchAsync()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
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

    private async Task ForceMatchStatusAsync(Guid matchId, string status)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await ctx.Database.ExecuteSqlRawAsync(
            "UPDATE matches SET status = {0} WHERE id = {1}", status, matchId);
    }

    /// <summary>Creates a Closed match, adds the fixed confirmed participants and drafts the teams.</summary>
    private async Task<Guid> SetupMatchWithTeamsAsync()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync();
        foreach (var p in Participants)
        {
            await AddConfirmedPresenceAsync(matchId, p);
        }
        await ForceMatchStatusAsync(matchId, "Closed");

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var draft = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft", null, TestContext.Current.CancellationToken);
        draft.StatusCode.Should().Be(HttpStatusCode.OK);
        return matchId;
    }

    private async Task<(Guid TeamAId, Guid TeamBId)> GetTeamsAsync(Guid matchId)
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync($"/api/v1/matches/{matchId}/teams", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var teams = body.GetProperty("teams");
        var teamAId = Enumerable.Range(0, teams.GetArrayLength())
            .Select(i => teams[i]).First(t => t.GetProperty("name").GetString() == "Team A")
            .GetProperty("id").GetGuid();
        var teamBId = Enumerable.Range(0, teams.GetArrayLength())
            .Select(i => teams[i]).First(t => t.GetProperty("name").GetString() == "Team B")
            .GetProperty("id").GetGuid();
        return (teamAId, teamBId);
    }

    /// <summary>Creates a BestOf3 scoreboard, starts it and plays Team A to a 2-0 win (25-0, 25-0).</summary>
    private async Task PlayFullBestOf3Async(Guid matchId, Guid teamAId)
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        (await client.PostAsJsonAsync($"/api/v1/matches/{matchId}/scoreboard", new { Format = "BestOf3" },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsync($"/api/v1/matches/{matchId}/scoreboard/start", null,
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        await RecordPointsAsync(client, matchId, setNumber: 1, teamAId, 25);
        await RecordPointsAsync(client, matchId, setNumber: 2, teamAId, 25);
    }

    private static async Task RecordPointsAsync(HttpClient client, Guid matchId, int setNumber, Guid teamId, int count)
    {
        for (var i = 0; i < count; i++)
        {
            var response = await client.PostAsJsonAsync(
                $"/api/v1/matches/{matchId}/scoreboard/sets/{setNumber}/points",
                new { TeamId = teamId },
                TestContext.Current.CancellationToken);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    /// <summary>
    /// Full pipeline: Closed match with drafted teams, an Ended BestOf3 scoreboard (Team A 2-0), and a
    /// Closed MVP voting session where P2 wins 3 of 4 votes. Ready for the summary to be generated.
    /// </summary>
    private async Task<SummarizableMatch> SetupSummarizableMatchAsync()
    {
        var matchId = await SetupMatchWithTeamsAsync();
        var teams = await GetTeamsAsync(matchId);
        await PlayFullBestOf3Async(matchId, teams.TeamAId);

        var organizer = _fx.CreateAuthenticatedClient(OrganizerUserId);
        (await organizer.PostAsJsonAsync($"/api/v1/matches/{matchId}/mvp-voting", new { DeadlineAt = (DateTimeOffset?)null },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Created);

        // P2 gets 3 votes; P2 votes P3 → total 4 votes, MVP = P2.
        await CastVoteAsync(matchId, P1, P2);
        await CastVoteAsync(matchId, P3, P2);
        await CastVoteAsync(matchId, P4, P2);
        await CastVoteAsync(matchId, P2, P3);

        (await organizer.PostAsync($"/api/v1/matches/{matchId}/mvp-voting/close", null,
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);

        _fx.Publisher.ClearReceivedCalls(); // discard setup events; assert only summary events
        return new SummarizableMatch(matchId, teams.TeamAId, teams.TeamBId);
    }

    private async Task CastVoteAsync(Guid matchId, Guid voterId, Guid votedId)
    {
        var client = _fx.CreateAuthenticatedClient(voterId);
        var response = await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/mvp-voting/votes",
            new { VotedPlayerId = votedId },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private sealed record SummarizableMatch(Guid MatchId, Guid TeamAId, Guid TeamBId);

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
