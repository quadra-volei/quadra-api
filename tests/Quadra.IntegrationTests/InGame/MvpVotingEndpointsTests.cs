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

namespace Quadra.IntegrationTests.InGame;

/// <summary>
/// HTTP integration tests for the F1.5 post-match MVP voting endpoints, backed by a real Postgres.
///
/// Coverage per acceptance criterion:
///
/// AC-1 "each player votes for ONE name (cannot self-vote)":
///   - a participant casts a single vote (200) that is persisted; total_votes = 1;
///   - re-voting while Open replaces the ballot and leaves total_votes unchanged (one vote per voter);
///   - a self-vote returns 409; a non-participant voter returns 403;
///   - a vote for a non-participant returns 404.
///
/// AC-2 "automatic MVP selection from votes":
///   - close tallies the votes and picks the highest-voted participant; MvpAwarded is published;
///   - a tie is broken by the lowest PlayerId (deterministic);
///   - with zero votes MvpPlayerId is null and NO MvpAwarded event is published.
///
/// AC-3 "voting deadline (set by Organizer; default 24h)":
///   - open persists the request deadline when provided; defaults to opened_at + 24h when omitted;
///   - a vote after the deadline returns 409.
///
/// Also: open requires organizer (403) and an ended scoreboard (409); duplicate open (409);
/// tallies hidden while Open and revealed once Closed; 401/404 error paths.
/// </summary>
public sealed class MvpVotingEndpointsTests : IClassFixture<InGameWebApplicationFactory>
{
    private readonly InGameWebApplicationFactory _fx;

    private static readonly Guid OrganizerUserId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Outsider = new("99999999-9999-9999-9999-999999999999");

    // Fixed, ascending participant ids so tie-break-by-lowest-id is deterministic.
    private static readonly Guid P1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid P2 = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid P3 = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid P4 = new("44444444-4444-4444-4444-444444444444");
    private static readonly Guid[] Participants = [P1, P2, P3, P4];

    public MvpVotingEndpointsTests(InGameWebApplicationFactory fx)
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
            "TRUNCATE TABLE mvp_votes, mvp_votings, scoreboard_sets, scoreboards, team_members, teams RESTART IDENTITY CASCADE;");
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

    /// <summary>Creates a Closed match, adds the fixed confirmed participants and drafts the teams.</summary>
    private async Task<Guid> SetupMatchWithTeamsAsync()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        foreach (var p in Participants)
        {
            await AddConfirmedPresenceAsync(matchId, p);
        }
        await ForceMatchClosedAsync(matchId);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var draft = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft", null, TestContext.Current.CancellationToken);
        draft.StatusCode.Should().Be(HttpStatusCode.OK);
        return matchId;
    }

    /// <summary>Creates a BestOf3 scoreboard, starts and force-ends it (state → Ended).</summary>
    private async Task EndScoreboardAsync(Guid matchId)
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        (await client.PostAsJsonAsync($"/api/v1/matches/{matchId}/scoreboard", new { Format = "BestOf3" },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await client.PostAsync($"/api/v1/matches/{matchId}/scoreboard/start", null,
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.PostAsync($"/api/v1/matches/{matchId}/scoreboard/end", null,
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>Creates a Closed match with teams AND an ended scoreboard, ready for MVP voting.</summary>
    private async Task<Guid> SetupEndedMatchAsync()
    {
        var matchId = await SetupMatchWithTeamsAsync();
        await EndScoreboardAsync(matchId);
        return matchId;
    }

    private async Task<HttpResponseMessage> OpenVotingAsync(
        Guid matchId, Guid callerId, DateTimeOffset? deadline = null)
    {
        var client = _fx.CreateAuthenticatedClient(callerId);
        return await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/mvp-voting",
            new { DeadlineAt = deadline },
            TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> CastVoteAsync(Guid matchId, Guid voterId, Guid votedId)
    {
        var client = _fx.CreateAuthenticatedClient(voterId);
        return await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/mvp-voting/votes",
            new { VotedPlayerId = votedId },
            TestContext.Current.CancellationToken);
    }

    private async Task<HttpResponseMessage> CloseVotingAsync(Guid matchId, Guid callerId)
    {
        var client = _fx.CreateAuthenticatedClient(callerId);
        return await client.PostAsync(
            $"/api/v1/matches/{matchId}/mvp-voting/close", null, TestContext.Current.CancellationToken);
    }

    private async Task<JsonElement> GetVotingAsync(Guid matchId, Guid callerId)
    {
        var client = _fx.CreateAuthenticatedClient(callerId);
        var response = await client.GetAsync(
            $"/api/v1/matches/{matchId}/mvp-voting", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
    }

    private async Task ExpireDeadlineAsync(Guid matchId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();
        var past = DateTimeOffset.UtcNow.AddHours(-1);
        await ctx.Database.ExecuteSqlAsync(
            $"UPDATE mvp_votings SET deadline_at = {past} WHERE match_id = {matchId}",
            TestContext.Current.CancellationToken);
    }

    // ═══ OPEN ════════════════════════════════════════════════════════════════

    /// <summary>Covers: F1.5 — 401 when no JWT is provided for POST /mvp-voting.</summary>
    [Fact]
    public async Task POST_open_without_JWT_returns_401()
    {
        var client = _fx.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync(
            $"/api/v1/matches/{Guid.NewGuid()}/mvp-voting", new { DeadlineAt = (DateTimeOffset?)null },
            TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Covers: F1.5 — 404 when the match does not exist.</summary>
    [Fact]
    public async Task POST_open_with_nonexistent_match_returns_404()
    {
        await ResetAsync();
        var response = await OpenVotingAsync(Guid.NewGuid(), OrganizerUserId);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>Covers: F1.5 "open requires organizer" — 403 when the caller is not the organizer.</summary>
    [Fact]
    public async Task POST_open_by_non_organizer_returns_403()
    {
        var matchId = await SetupEndedMatchAsync();
        var response = await OpenVotingAsync(matchId, P1); // a participant, not the organizer
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Covers: F1.5 "open requires ended scoreboard" — 409 when the scoreboard is not Ended.</summary>
    [Fact]
    public async Task POST_open_when_scoreboard_not_ended_returns_409()
    {
        var matchId = await SetupMatchWithTeamsAsync();
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        // Create a scoreboard but leave it NotStarted (not Ended).
        (await client.PostAsJsonAsync($"/api/v1/matches/{matchId}/scoreboard", new { Format = "BestOf3" },
            TestContext.Current.CancellationToken)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await OpenVotingAsync(matchId, OrganizerUserId);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Covers: F1.5 — 400 when the provided deadline is in the past.</summary>
    [Fact]
    public async Task POST_open_with_past_deadline_returns_400()
    {
        var matchId = await SetupEndedMatchAsync();
        var response = await OpenVotingAsync(matchId, OrganizerUserId, DateTimeOffset.UtcNow.AddDays(-1));
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Covers: F1.5 AC-3 — open with no deadline defaults it to opened_at + 24h, returns 201 and persists.
    /// </summary>
    [Fact]
    public async Task POST_open_without_deadline_defaults_to_opened_plus_24h_and_persists()
    {
        var matchId = await SetupEndedMatchAsync();

        var response = await OpenVotingAsync(matchId, OrganizerUserId);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("state").GetString().Should().Be("Open");
        body.GetProperty("totalVotes").GetInt32().Should().Be(0);

        var openedAt = body.GetProperty("openedAt").GetDateTimeOffset();
        var deadlineAt = body.GetProperty("deadlineAt").GetDateTimeOffset();
        (deadlineAt - openedAt).Should().Be(TimeSpan.FromHours(24));

        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();
        (await ctx.MvpVotings.CountAsync(v => v.MatchId == matchId, TestContext.Current.CancellationToken))
            .Should().Be(1);
    }

    /// <summary>Covers: F1.5 AC-3 — open persists the Organizer-provided deadline verbatim.</summary>
    [Fact]
    public async Task POST_open_with_deadline_persists_that_deadline()
    {
        var matchId = await SetupEndedMatchAsync();
        var deadline = DateTimeOffset.UtcNow.AddHours(48);

        var response = await OpenVotingAsync(matchId, OrganizerUserId, deadline);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("deadlineAt").GetDateTimeOffset()
            .Should().BeCloseTo(deadline, TimeSpan.FromMilliseconds(1));
    }

    /// <summary>Covers: F1.5 — 409 when a voting session already exists for the match.</summary>
    [Fact]
    public async Task POST_open_twice_returns_409()
    {
        var matchId = await SetupEndedMatchAsync();

        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ═══ CAST VOTE ═════════════════════════════════════════════════════════════

    /// <summary>Covers: F1.5 — 404 when voting for a match with no open session.</summary>
    [Fact]
    public async Task POST_vote_without_open_voting_returns_404()
    {
        var matchId = await SetupEndedMatchAsync();
        var response = await CastVoteAsync(matchId, P1, P2);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers: F1.5 AC-1 — a participant casts a single vote (200), it persists, and total_votes = 1;
    /// GET reports CallerHasVoted = true for that voter.
    /// </summary>
    [Fact]
    public async Task POST_vote_by_participant_returns_200_and_persists()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await CastVoteAsync(matchId, P1, P2);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("voterPlayerId").GetGuid().Should().Be(P1);
        body.GetProperty("votedPlayerId").GetGuid().Should().Be(P2);

        var voting = await GetVotingAsync(matchId, P1);
        voting.GetProperty("totalVotes").GetInt32().Should().Be(1);
        voting.GetProperty("callerHasVoted").GetBoolean().Should().BeTrue();

        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();
        (await ctx.MvpVotes.CountAsync(
            v => v.MatchId == matchId && v.VoterPlayerId == P1, TestContext.Current.CancellationToken))
            .Should().Be(1);
    }

    /// <summary>Covers: F1.5 AC-1 — a non-participant voter is rejected with 403.</summary>
    [Fact]
    public async Task POST_vote_by_non_participant_returns_403()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await CastVoteAsync(matchId, Outsider, P2);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Covers: F1.5 AC-1 — a self-vote is rejected with 409.</summary>
    [Fact]
    public async Task POST_vote_for_self_returns_409()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await CastVoteAsync(matchId, P1, P1);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Covers: F1.5 AC-1 — voting for a non-participant is rejected with 404.</summary>
    [Fact]
    public async Task POST_vote_for_non_participant_returns_404()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await CastVoteAsync(matchId, P1, Outsider);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers: F1.5 AC-1 — re-voting while Open replaces the ballot (one vote per voter):
    /// the voted player changes but total_votes stays at 1.
    /// </summary>
    [Fact]
    public async Task POST_vote_twice_replaces_ballot_and_keeps_total_votes_unchanged()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await CastVoteAsync(matchId, P1, P2)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CastVoteAsync(matchId, P1, P3)).StatusCode.Should().Be(HttpStatusCode.OK);

        var voting = await GetVotingAsync(matchId, P1);
        voting.GetProperty("totalVotes").GetInt32().Should().Be(1, "re-voting replaces the single ballot");

        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();
        var votes = await ctx.MvpVotes
            .Where(v => v.MatchId == matchId && v.VoterPlayerId == P1)
            .ToListAsync(TestContext.Current.CancellationToken);
        votes.Should().ContainSingle();
        votes[0].VotedPlayerId.Should().Be(P3, "the ballot now points at the replacement candidate");
    }

    /// <summary>Covers: F1.5 AC-3 — a vote cast after the deadline is rejected with 409.</summary>
    [Fact]
    public async Task POST_vote_after_deadline_returns_409()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);
        await ExpireDeadlineAsync(matchId);

        var response = await CastVoteAsync(matchId, P1, P2);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>Covers: F1.5 — 409 when voting on a session that is already Closed.</summary>
    [Fact]
    public async Task POST_vote_when_closed_returns_409()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CloseVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await CastVoteAsync(matchId, P1, P2);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ═══ GET (tally visibility) ══════════════════════════════════════════════

    /// <summary>
    /// Covers: F1.5 — while Open, per-candidate tallies are hidden (Results empty, MvpPlayerId null)
    /// even after votes have been cast.
    /// </summary>
    [Fact]
    public async Task GET_while_open_hides_tallies()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);
        (await CastVoteAsync(matchId, P1, P2)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CastVoteAsync(matchId, P3, P2)).StatusCode.Should().Be(HttpStatusCode.OK);

        var voting = await GetVotingAsync(matchId, P1);
        voting.GetProperty("state").GetString().Should().Be("Open");
        voting.GetProperty("results").GetArrayLength().Should().Be(0, "tallies are hidden while Open");
        voting.GetProperty("mvpPlayerId").ValueKind.Should().Be(JsonValueKind.Null);
        voting.GetProperty("totalVotes").GetInt32().Should().Be(2);
    }

    // ═══ CLOSE (AC-2) ═════════════════════════════════════════════════════════

    /// <summary>Covers: F1.5 "close requires organizer" — 403 when the caller is not the organizer.</summary>
    [Fact]
    public async Task POST_close_by_non_organizer_returns_403()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);

        var response = await CloseVotingAsync(matchId, P1);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>Covers: F1.5 — 409 when closing an already-closed session.</summary>
    [Fact]
    public async Task POST_close_twice_returns_409()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);

        (await CloseVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CloseVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Covers: F1.5 AC-2 — close tallies the votes, selects the highest-voted participant as MVP,
    /// publishes MvpAwarded, and GET reveals the per-candidate tallies once Closed.
    /// </summary>
    [Fact]
    public async Task POST_close_selects_highest_voted_mvp_and_publishes_event()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);

        // P2 receives 3 votes; P3 receives 1 → MVP is P2 with 3 votes, total 4 votes.
        (await CastVoteAsync(matchId, P1, P2)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CastVoteAsync(matchId, P3, P2)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CastVoteAsync(matchId, P4, P2)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CastVoteAsync(matchId, P2, P3)).StatusCode.Should().Be(HttpStatusCode.OK);

        var closeResponse = await CloseVotingAsync(matchId, OrganizerUserId);
        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await closeResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("state").GetString().Should().Be("Closed");
        body.GetProperty("mvpPlayerId").GetGuid().Should().Be(P2);
        body.GetProperty("mvpVoteCount").GetInt32().Should().Be(3);
        body.GetProperty("totalVotes").GetInt32().Should().Be(4);

        // GET now reveals the tallies.
        var voting = await GetVotingAsync(matchId, OrganizerUserId);
        var results = voting.GetProperty("results");
        results.GetArrayLength().Should().Be(2);
        results[0].GetProperty("playerId").GetGuid().Should().Be(P2, "results are ordered by votes desc");
        results[0].GetProperty("votes").GetInt32().Should().Be(3);

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<MvpAwarded>(e =>
                e.MatchId == matchId && e.PlayerId == P2 && e.VoteCount == 3 && e.TotalVotes == 4),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.5 AC-2 — a tie on vote count is broken by the lowest PlayerId (P2 &lt; P3),
    /// deterministically selecting P2.
    /// </summary>
    [Fact]
    public async Task POST_close_breaks_tie_by_lowest_player_id()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);

        // P2 and P3 each receive one vote → tie broken by lowest id → P2.
        (await CastVoteAsync(matchId, P1, P2)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await CastVoteAsync(matchId, P4, P3)).StatusCode.Should().Be(HttpStatusCode.OK);

        var closeResponse = await CloseVotingAsync(matchId, OrganizerUserId);
        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await closeResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("mvpPlayerId").GetGuid().Should().Be(P2, "ties resolve to the lowest PlayerId");
        body.GetProperty("mvpVoteCount").GetInt32().Should().Be(1);

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<MvpAwarded>(e => e.MatchId == matchId && e.PlayerId == P2),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.5 AC-2 — closing with zero votes leaves MvpPlayerId null and publishes NO MvpAwarded.
    /// </summary>
    [Fact]
    public async Task POST_close_with_zero_votes_has_null_mvp_and_publishes_nothing()
    {
        var matchId = await SetupEndedMatchAsync();
        (await OpenVotingAsync(matchId, OrganizerUserId)).StatusCode.Should().Be(HttpStatusCode.Created);
        _fx.Publisher.ClearReceivedCalls(); // discard TeamsFormed/MatchStarted/MatchEnded from setup

        var closeResponse = await CloseVotingAsync(matchId, OrganizerUserId);
        closeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await closeResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("state").GetString().Should().Be("Closed");
        body.GetProperty("mvpPlayerId").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("mvpVoteCount").ValueKind.Should().Be(JsonValueKind.Null);

        await _fx.Publisher.DidNotReceive().PublishAsync(
            Arg.Any<MvpAwarded>(), Arg.Any<CancellationToken>());
    }

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
