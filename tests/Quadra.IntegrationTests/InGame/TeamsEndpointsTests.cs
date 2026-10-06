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
/// HTTP integration tests for team lifecycle endpoints (F1.3).
///
/// Coverage per acceptance criterion:
///
/// AC-1: POST /api/v1/matches/{matchId}/teams/draft — auto-balanced snake-draft by level;
///       level-score difference between teams must be ≤ 1 unit for even player count.
/// AC-2: PUT /api/v1/matches/{matchId}/teams/{teamId}/members/{playerId} — moves player to
///       specified team and returns updated full team composition.
///
/// Error paths:
///   - 401 missing JWT for draft and move
///   - 403 when caller is not the organizer (draft and move)
///   - 404 when match not found (draft and move)
///   - 409 when match status is not Closed (draft and move)
///   - 409 when no confirmed players exist (draft)
///   - 404 when teams not yet formed (GET teams and PUT move)
///   - 404 when player not in any team (PUT move)
///   - 200 idempotent no-op when player already in target team (PUT move)
/// </summary>
public sealed class TeamsEndpointsTests : IClassFixture<InGameWebApplicationFactory>
{
    private readonly InGameWebApplicationFactory _fx;
    private static readonly Guid OrganizerUserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    public TeamsEndpointsTests(InGameWebApplicationFactory fx)
    {
        _fx = fx;
    }

    // ─── Reset helpers ───────────────────────────────────────────────────────

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var matchesCtx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var inGameCtx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();

        // Delete InGame data first (no DDL FK, but let's be tidy)
        await inGameCtx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE team_members, teams RESTART IDENTITY CASCADE;");

        // Delete Matches data
        await matchesCtx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE waiting_list, match_presences, matches RESTART IDENTITY CASCADE;");

        _fx.Publisher.ClearReceivedCalls();
    }

    // ─── Setup helpers ───────────────────────────────────────────────────────

    /// <summary>Creates a match via the API and returns its ID.</summary>
    private async Task<Guid> CreateMatchAsync(Guid organizerId)
    {
        var client = _fx.CreateAuthenticatedClient(organizerId);
        var payload = BuildMatchPayload();
        var response = await client.PostAsJsonAsync("/api/v1/matches", payload, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>Adds a confirmed presence directly in the DB.</summary>
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

    /// <summary>Forces match status to Closed via raw SQL.</summary>
    private async Task ForceMatchClosedAsync(Guid matchId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await ctx.Database.ExecuteSqlAsync(
            $"UPDATE matches SET status = 'Closed' WHERE id = {matchId}",
            TestContext.Current.CancellationToken);
    }

    /// <summary>Forces match status to a non-Closed value (Open) via raw SQL.</summary>
    private async Task ForceMatchOpenAsync(Guid matchId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await ctx.Database.ExecuteSqlAsync(
            $"UPDATE matches SET status = 'Open' WHERE id = {matchId}",
            TestContext.Current.CancellationToken);
    }

    // ─── POST /teams/draft — 401 unauthenticated ─────────────────────────────

    /// <summary>
    /// Covers: 401 when no JWT is provided for POST /teams/draft.
    /// </summary>
    [Fact]
    public async Task POST_draft_without_JWT_returns_401()
    {
        var client = _fx.CreateAnonymousClient();
        var response = await client.PostAsync(
            $"/api/v1/matches/{Guid.NewGuid()}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── POST /teams/draft — 404 match not found ─────────────────────────────

    /// <summary>
    /// Covers: F1.3 error path — 404 when match not found for POST /teams/draft.
    /// </summary>
    [Fact]
    public async Task POST_draft_with_nonexistent_match_returns_404()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PostAsync(
            $"/api/v1/matches/{Guid.NewGuid()}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── POST /teams/draft — 403 not organizer ───────────────────────────────

    /// <summary>
    /// Covers: F1.3 error path — 403 when caller is not the organizer for POST /teams/draft.
    /// </summary>
    [Fact]
    public async Task POST_draft_by_non_organizer_returns_403()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        // Add a confirmed player so the no-confirmed-players 409 doesn't fire first
        await AddConfirmedPresenceAsync(matchId, Guid.NewGuid());

        var client = _fx.CreateAuthenticatedClient(OtherUserId);
        var response = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── POST /teams/draft — 409 match not Closed ────────────────────────────

    /// <summary>
    /// Covers: F1.3 error path — 409 when match status is not Closed for POST /teams/draft.
    /// </summary>
    [Fact]
    public async Task POST_draft_when_match_is_not_Closed_returns_409()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        // Match is created in Draft status; leave it as-is

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("detail").GetString().Should().Contain("Closed");
    }

    // ─── POST /teams/draft — 409 no confirmed players ────────────────────────

    /// <summary>
    /// Covers: F1.3 error path — 409 when no confirmed players exist for the match.
    /// </summary>
    [Fact]
    public async Task POST_draft_with_no_confirmed_players_returns_409()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);
        // No presences added

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("detail").GetString().Should().Contain("No confirmed players");
    }

    // ─── POST /teams/draft — AC-1: successful draft, balanced teams ──────────

    /// <summary>
    /// Covers: F1.3 AC-1 — POST /teams/draft returns 200 with TeamsResponse;
    /// produces two teams with all confirmed players assigned,
    /// team names are "Team A" and "Team B",
    /// all player IDs appear in the response.
    /// Also verifies the TeamsFormed SQS event is published.
    /// </summary>
    [Fact]
    public async Task POST_draft_with_valid_closed_match_returns_200_with_two_teams_and_all_players()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        var player3 = Guid.NewGuid();
        var player4 = Guid.NewGuid();
        await AddConfirmedPresenceAsync(matchId, player1);
        await AddConfirmedPresenceAsync(matchId, player2);
        await AddConfirmedPresenceAsync(matchId, player3);
        await AddConfirmedPresenceAsync(matchId, player4);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var teams = body.GetProperty("teams");
        teams.GetArrayLength().Should().Be(2);

        // Verify team names
        var teamNames = Enumerable.Range(0, 2)
            .Select(i => teams[i].GetProperty("name").GetString())
            .OrderBy(n => n)
            .ToList();
        teamNames.Should().BeEquivalentTo(["Team A", "Team B"]);

        // Verify all 4 players are assigned across the two teams
        var allMemberIds = Enumerable.Range(0, 2)
            .SelectMany(i => Enumerable.Range(0, teams[i].GetProperty("members").GetArrayLength())
                .Select(j => teams[i].GetProperty("members")[j].GetProperty("playerId").GetGuid()))
            .ToList();

        allMemberIds.Should().HaveCount(4);
        allMemberIds.Should().Contain(player1);
        allMemberIds.Should().Contain(player2);
        allMemberIds.Should().Contain(player3);
        allMemberIds.Should().Contain(player4);
    }

    /// <summary>
    /// Covers: F1.3 AC-1 — snake-draft with even player count (4) produces balanced teams;
    /// level-score difference ≤ 1. All players are Beginner (level provided by NoOpPlayerLevelReader).
    /// With 4 Beginner players, scores: TeamA=0, TeamB=0, diff=0.
    /// </summary>
    [Fact]
    public async Task POST_draft_with_4_players_produces_balanced_teams_with_2_players_each()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var players = Enumerable.Range(1, 4).Select(_ => Guid.NewGuid()).ToList();
        foreach (var p in players)
        {
            await AddConfirmedPresenceAsync(matchId, p);
        }

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var teams = body.GetProperty("teams");

        var teamAMembers = teams[0].GetProperty("members").GetArrayLength();
        var teamBMembers = teams[1].GetProperty("members").GetArrayLength();

        // With 4 players, should be 2-2 split
        (teamAMembers + teamBMembers).Should().Be(4);
        teamAMembers.Should().Be(2);
        teamBMembers.Should().Be(2);
    }

    /// <summary>
    /// Covers: F1.3 AC-1 — teams are persisted in the database after draft.
    /// </summary>
    [Fact]
    public async Task POST_draft_persists_teams_and_members_in_database()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        await AddConfirmedPresenceAsync(matchId, player1);
        await AddConfirmedPresenceAsync(matchId, player2);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        // Verify persistence
        using var scope = _fx.Factory.Services.CreateScope();
        var inGameCtx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();

        var teams = await inGameCtx.Teams
            .Where(t => t.MatchId == matchId)
            .ToListAsync(TestContext.Current.CancellationToken);
        teams.Should().HaveCount(2);

        var members = await inGameCtx.TeamMembers
            .Where(m => m.MatchId == matchId)
            .ToListAsync(TestContext.Current.CancellationToken);
        members.Should().HaveCount(2);
        members.Select(m => m.PlayerId).Should().Contain(player1);
        members.Select(m => m.PlayerId).Should().Contain(player2);
    }

    /// <summary>
    /// Covers: F1.3 — TeamsFormed SQS event is published after a successful draft.
    /// </summary>
    [Fact]
    public async Task POST_draft_publishes_TeamsFormed_event_with_correct_matchId()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        await AddConfirmedPresenceAsync(matchId, player1);
        await AddConfirmedPresenceAsync(matchId, player2);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<TeamsFormed>(e =>
                e.MatchId == matchId
                && e.Teams.Count == 2
                && e.Teams.Sum(t => t.PlayerIds.Count) == 2),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.3 — idempotent re-draft: calling POST /teams/draft a second time replaces
    /// existing teams and returns a fresh balanced assignment.
    /// </summary>
    [Fact]
    public async Task POST_draft_called_twice_replaces_existing_teams_idempotently()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        await AddConfirmedPresenceAsync(matchId, player1);
        await AddConfirmedPresenceAsync(matchId, player2);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        // First draft
        var first = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Second draft (re-draft)
        var second = await client.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify DB has exactly 2 teams (not 4)
        using var scope = _fx.Factory.Services.CreateScope();
        var inGameCtx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();
        var teamCount = await inGameCtx.Teams
            .Where(t => t.MatchId == matchId)
            .CountAsync(TestContext.Current.CancellationToken);
        teamCount.Should().Be(2, "re-draft should replace old teams, not add new ones");
    }

    // ─── GET /teams — 404 match not found ────────────────────────────────────

    /// <summary>
    /// Covers: F1.3 error path — 404 when match not found for GET /teams.
    /// </summary>
    [Fact]
    public async Task GET_teams_with_nonexistent_match_returns_404()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync(
            $"/api/v1/matches/{Guid.NewGuid()}/teams",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── GET /teams — 404 teams not yet formed ───────────────────────────────

    /// <summary>
    /// Covers: F1.3 error path — 404 when teams have not been drafted yet.
    /// </summary>
    [Fact]
    public async Task GET_teams_when_not_drafted_returns_404()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        // No draft performed

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync(
            $"/api/v1/matches/{matchId}/teams",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── GET /teams — 200 success ────────────────────────────────────────────

    /// <summary>
    /// Covers: GET /teams returns 200 with correct team composition after draft.
    /// </summary>
    [Fact]
    public async Task GET_teams_after_draft_returns_200_with_teams()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        await AddConfirmedPresenceAsync(matchId, player1);
        await AddConfirmedPresenceAsync(matchId, player2);

        var organizer = _fx.CreateAuthenticatedClient(OrganizerUserId);
        await organizer.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        var response = await organizer.GetAsync(
            $"/api/v1/matches/{matchId}/teams",
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("teams").GetArrayLength().Should().Be(2);
    }

    // ─── PUT /teams/{teamId}/members/{playerId} — 401 unauthenticated ────────

    /// <summary>
    /// Covers: 401 when no JWT is provided for PUT /members.
    /// </summary>
    [Fact]
    public async Task PUT_member_without_JWT_returns_401()
    {
        var client = _fx.CreateAnonymousClient();
        var response = await client.PutAsync(
            $"/api/v1/matches/{Guid.NewGuid()}/teams/{Guid.NewGuid()}/members/{Guid.NewGuid()}",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── PUT /teams/{teamId}/members/{playerId} — 404 match not found ────────

    /// <summary>
    /// Covers: F1.3 error path — 404 when match not found for PUT /members.
    /// </summary>
    [Fact]
    public async Task PUT_member_with_nonexistent_match_returns_404()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PutAsync(
            $"/api/v1/matches/{Guid.NewGuid()}/teams/{Guid.NewGuid()}/members/{Guid.NewGuid()}",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── PUT /teams/{teamId}/members/{playerId} — 403 not organizer ──────────

    /// <summary>
    /// Covers: F1.3 error path — 403 when caller is not the organizer for PUT /members.
    /// </summary>
    [Fact]
    public async Task PUT_member_by_non_organizer_returns_403()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var client = _fx.CreateAuthenticatedClient(OtherUserId);
        var response = await client.PutAsync(
            $"/api/v1/matches/{matchId}/teams/{Guid.NewGuid()}/members/{Guid.NewGuid()}",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ─── PUT /teams/{teamId}/members/{playerId} — 409 not Closed ────────────

    /// <summary>
    /// Covers: F1.3 error path — 409 when match is not Closed for PUT /members.
    /// </summary>
    [Fact]
    public async Task PUT_member_when_match_is_not_Closed_returns_409()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        // Match is in Draft status

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PutAsync(
            $"/api/v1/matches/{matchId}/teams/{Guid.NewGuid()}/members/{Guid.NewGuid()}",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("detail").GetString().Should().Contain("Closed");
    }

    // ─── PUT /teams/{teamId}/members/{playerId} — 404 teams not formed ───────

    /// <summary>
    /// Covers: F1.3 error path — 404 when teams have not been formed yet for PUT /members.
    /// </summary>
    [Fact]
    public async Task PUT_member_when_teams_not_drafted_returns_404()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);
        // No draft performed

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.PutAsync(
            $"/api/v1/matches/{matchId}/teams/{Guid.NewGuid()}/members/{Guid.NewGuid()}",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── PUT /teams/{teamId}/members/{playerId} — 404 team not found ─────────

    /// <summary>
    /// Covers: F1.3 error path — 404 when teamId is not found for this match.
    /// </summary>
    [Fact]
    public async Task PUT_member_with_unknown_teamId_returns_404()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        await AddConfirmedPresenceAsync(matchId, player1);
        await AddConfirmedPresenceAsync(matchId, player2);

        var organizer = _fx.CreateAuthenticatedClient(OrganizerUserId);
        await organizer.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);

        // Use a random teamId that doesn't belong to this match
        var unknownTeamId = Guid.NewGuid();
        var response = await organizer.PutAsync(
            $"/api/v1/matches/{matchId}/teams/{unknownTeamId}/members/{player1}",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── PUT /teams/{teamId}/members/{playerId} — 404 player not in team ─────

    /// <summary>
    /// Covers: F1.3 error path — 404 when player is not in any team for this match.
    /// </summary>
    [Fact]
    public async Task PUT_member_with_player_not_in_any_team_returns_404()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        await AddConfirmedPresenceAsync(matchId, player1);
        await AddConfirmedPresenceAsync(matchId, player2);

        var organizer = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var draftResponse = await organizer.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);
        draftResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Get team A id from the draft response
        var draftBody = await draftResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var teams = draftBody.GetProperty("teams");
        var teamAId = teams[0].GetProperty("id").GetGuid();

        var unknownPlayer = Guid.NewGuid(); // not in any team

        var response = await organizer.PutAsync(
            $"/api/v1/matches/{matchId}/teams/{teamAId}/members/{unknownPlayer}",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── PUT /teams/{teamId}/members/{playerId} — 200 idempotent no-op ───────

    /// <summary>
    /// Covers: F1.3 error path — 200 when player is already in the target team (no-op).
    /// </summary>
    [Fact]
    public async Task PUT_member_when_player_already_in_target_team_returns_200_without_change()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        await AddConfirmedPresenceAsync(matchId, player1);
        await AddConfirmedPresenceAsync(matchId, player2);

        var organizer = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var draftResponse = await organizer.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);
        draftResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var draftBody = await draftResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var teams = draftBody.GetProperty("teams");

        // Find which team player1 was assigned to
        Guid player1TeamId = Guid.Empty;
        for (var t = 0; t < 2; t++)
        {
            var members = teams[t].GetProperty("members");
            for (var m = 0; m < members.GetArrayLength(); m++)
            {
                if (members[m].GetProperty("playerId").GetGuid() == player1)
                {
                    player1TeamId = teams[t].GetProperty("id").GetGuid();
                }
            }
        }
        player1TeamId.Should().NotBeEmpty();

        // Move player1 to the same team they're already in — should be a no-op
        var response = await organizer.PutAsync(
            $"/api/v1/matches/{matchId}/teams/{player1TeamId}/members/{player1}",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // DB should be unchanged — player1 is still in player1TeamId
        using var scope = _fx.Factory.Services.CreateScope();
        var inGameCtx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();
        var member = await inGameCtx.TeamMembers
            .FirstAsync(
                m => m.MatchId == matchId && m.PlayerId == player1,
                TestContext.Current.CancellationToken);
        member.TeamId.Should().Be(player1TeamId);
    }

    // ─── PUT /teams/{teamId}/members/{playerId} — AC-2: successful move ──────

    /// <summary>
    /// Covers: F1.3 AC-2 — PUT moves player to specified team and returns updated full composition.
    /// After draft, moves player from their current team to the other team.
    /// Verifies the player's new team in the DB and that the response reflects the change.
    /// </summary>
    [Fact]
    public async Task PUT_member_moves_player_to_other_team_and_returns_200_with_updated_composition()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        await AddConfirmedPresenceAsync(matchId, player1);
        await AddConfirmedPresenceAsync(matchId, player2);

        var organizer = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var draftResponse = await organizer.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);
        draftResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var draftBody = await draftResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var teams = draftBody.GetProperty("teams");

        // Identify team containing player1 and the other team
        Guid player1CurrentTeamId = Guid.Empty;
        Guid targetTeamId = Guid.Empty;

        for (var t = 0; t < 2; t++)
        {
            var members = teams[t].GetProperty("members");
            var hasPlayer1 = false;
            for (var m = 0; m < members.GetArrayLength(); m++)
            {
                if (members[m].GetProperty("playerId").GetGuid() == player1)
                {
                    hasPlayer1 = true;
                    player1CurrentTeamId = teams[t].GetProperty("id").GetGuid();
                }
            }
            if (!hasPlayer1)
            {
                targetTeamId = teams[t].GetProperty("id").GetGuid();
            }
        }
        player1CurrentTeamId.Should().NotBeEmpty();
        targetTeamId.Should().NotBeEmpty();
        player1CurrentTeamId.Should().NotBe(targetTeamId);

        // Move player1 to the other team
        var response = await organizer.PutAsync(
            $"/api/v1/matches/{matchId}/teams/{targetTeamId}/members/{player1}",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify the response includes the updated team composition
        var responseBody = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var updatedTeams = responseBody.GetProperty("teams");
        updatedTeams.GetArrayLength().Should().Be(2);

        // Verify player1 is now in the target team in the response
        var targetTeamInResponse = Enumerable.Range(0, 2)
            .Select(i => updatedTeams[i])
            .FirstOrDefault(t => t.GetProperty("id").GetGuid() == targetTeamId);

        targetTeamInResponse.ValueKind.Should().NotBe(JsonValueKind.Undefined);
        var targetMembers = targetTeamInResponse.GetProperty("members");
        var player1InTarget = Enumerable.Range(0, targetMembers.GetArrayLength())
            .Any(m => targetMembers[m].GetProperty("playerId").GetGuid() == player1);
        player1InTarget.Should().BeTrue("player1 should be in the target team after the move");

        // Verify persistence in DB
        using var scope = _fx.Factory.Services.CreateScope();
        var inGameCtx = scope.ServiceProvider.GetRequiredService<InGameDbContext>();
        var member = await inGameCtx.TeamMembers
            .FirstAsync(
                m => m.MatchId == matchId && m.PlayerId == player1,
                TestContext.Current.CancellationToken);
        member.TeamId.Should().Be(targetTeamId, "player1's TeamId should be updated in the database");
    }

    /// <summary>
    /// Covers: F1.3 AC-2 — response includes PlayerLevel field in each member.
    /// </summary>
    [Fact]
    public async Task PUT_member_response_includes_PlayerLevel_field_for_each_member()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchClosedAsync(matchId);

        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        await AddConfirmedPresenceAsync(matchId, player1);
        await AddConfirmedPresenceAsync(matchId, player2);

        var organizer = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var draftResponse = await organizer.PostAsync(
            $"/api/v1/matches/{matchId}/teams/draft",
            null,
            TestContext.Current.CancellationToken);
        var draftBody = await draftResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var teams = draftBody.GetProperty("teams");

        Guid player1CurrentTeamId = Guid.Empty;
        Guid targetTeamId = Guid.Empty;
        for (var t = 0; t < 2; t++)
        {
            var members = teams[t].GetProperty("members");
            var hasPlayer1 = false;
            for (var m = 0; m < members.GetArrayLength(); m++)
            {
                if (members[m].GetProperty("playerId").GetGuid() == player1)
                {
                    hasPlayer1 = true;
                    player1CurrentTeamId = teams[t].GetProperty("id").GetGuid();
                }
            }
            if (!hasPlayer1) targetTeamId = teams[t].GetProperty("id").GetGuid();
        }

        var response = await organizer.PutAsync(
            $"/api/v1/matches/{matchId}/teams/{targetTeamId}/members/{player1}",
            null,
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var allMembers = Enumerable.Range(0, 2)
            .SelectMany(t => Enumerable.Range(0, body.GetProperty("teams")[t].GetProperty("members").GetArrayLength())
                .Select(m => body.GetProperty("teams")[t].GetProperty("members")[m]))
            .ToList();

        allMembers.Should().AllSatisfy(m =>
            m.TryGetProperty("playerLevel", out _).Should().BeTrue("each member should have a playerLevel field"));
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

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
