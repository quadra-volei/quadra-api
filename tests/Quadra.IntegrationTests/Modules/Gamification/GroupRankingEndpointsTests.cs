using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Quadra.Modules.Gamification.Contracts;
using Quadra.Modules.Matches.Entities;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.IntegrationTests.Modules.Gamification;

/// <summary>
/// HTTP integration tests for the F2.3 group-ranking endpoint
/// (<c>GET /api/v1/matches/{matchId}/ranking</c>), backed by a real Postgres. Standings are seeded
/// through the Gamification-owned write interface; the recurring/OneOff match is seeded through the
/// Matches DbContext (the endpoint validates it via <c>IMatchGroupReader</c>).
///
/// Coverage per acceptance criterion: AC-4, AC-5, AC-6, AC-7, AC-8, AC-10, AC-11, AC-12, AC-13,
/// AC-14, AC-15, AC-16, AC-17.
/// </summary>
[Collection("Gamification")]
public sealed class GroupRankingEndpointsTests
{
    private readonly GamificationWebApplicationFactory _fx;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public GroupRankingEndpointsTests(GamificationWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static string Url(Guid matchId, int? page = null, int? pageSize = null)
    {
        var query = new List<string>();
        if (page is { } p)
        {
            query.Add($"page={p}");
        }

        if (pageSize is { } ps)
        {
            query.Add($"pageSize={ps}");
        }

        var suffix = query.Count > 0 ? "?" + string.Join('&', query) : string.Empty;
        return $"/api/v1/matches/{matchId}/ranking{suffix}";
    }

    /// <summary>Seeds <paramref name="count"/> players with strictly descending unique point totals.
    /// Returns their ids ordered by rank (highest points first).</summary>
    private async Task<IReadOnlyList<Guid>> SeedDescendingAsync(Guid groupId, int count)
    {
        var ordered = new List<Guid>(count);
        for (var i = 0; i < count; i++)
        {
            var userId = Guid.NewGuid();
            ordered.Add(userId);
            await _fx.SeedRankingAsync(groupId, userId, totalPoints: (count - i) * 5, matchesCounted: 1);
        }

        return ordered;
    }

    // ═══ AC-17: auth ═══════════════════════════════════════════════════════════

    /// <summary>Covers AC-17 (auth): a missing JWT returns 401.</summary>
    [Fact]
    public async Task GET_ranking_without_JWT_returns_401()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring);
        var client = _fx.CreateAnonymousClient();

        var response = await client.GetAsync(Url(matchId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Covers AC-17 (auth): an invalid/wrongly-signed JWT returns 401.</summary>
    [Fact]
    public async Task GET_ranking_with_invalid_JWT_returns_401()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring);
        var client = _fx.CreateClientWithInvalidToken();

        var response = await client.GetAsync(Url(matchId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══ AC-6 / AC-7: match validation ═════════════════════════════════════════

    /// <summary>Covers AC-6 (unknown match): a matchId with no matches row returns 404.</summary>
    [Fact]
    public async Task GET_ranking_for_unknown_match_returns_404()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(Url(Guid.NewGuid()), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers AC-7 (OneOff): an existing OneOff match returns 409 and no ledger/standing rows are
    /// written for that match.
    /// </summary>
    [Fact]
    public async Task GET_ranking_for_oneoff_match_returns_409_and_writes_nothing()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.OneOff);
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(Url(matchId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await _fx.CountTransactionsAsync(matchId)).Should().Be(0);
        (await _fx.GetRankingRowAsync(matchId, Guid.NewGuid())).Should().BeNull();
    }

    // ═══ AC-8: empty group ═════════════════════════════════════════════════════

    /// <summary>
    /// Covers AC-8 (empty group): an existing recurring match with no finished matches yet returns 200
    /// with empty Items, TotalCount = 0, HasNextPage = false, CallerEntry = null.
    /// </summary>
    [Fact]
    public async Task GET_ranking_for_empty_recurring_group_returns_empty_200()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring, "Empty Group");
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(Url(matchId), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<GroupRankingResponse>(Ct);
        body.Should().NotBeNull();
        body!.GroupId.Should().Be(matchId);
        body.MatchName.Should().Be("Empty Group");
        body.Items.Should().BeEmpty();
        body.TotalCount.Should().Be(0);
        body.HasNextPage.Should().BeFalse();
        body.CallerEntry.Should().BeNull();
    }

    // ═══ AC-4 / AC-5: ordering, tiebreak, absolute rank ════════════════════════

    /// <summary>
    /// Covers AC-4 (ordering + tiebreak): Items are ordered by TotalPoints descending; two players with
    /// equal TotalPoints appear ordered by UserId ascending, and their Rank values follow that order.
    /// Also covers AC-5 (absolute rank): Items[i].Rank == (Page-1)*PageSize + i + 1.
    /// </summary>
    [Fact]
    public async Task GET_ranking_orders_by_points_desc_then_userid_asc_with_absolute_rank()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring);

        // Two tied players (50 pts) with controlled ids; one clear leader (60) and one trailer (10).
        var leader = Guid.NewGuid();
        var tieLow = new Guid("11111111-1111-1111-1111-111111111111");
        var tieHigh = new Guid("22222222-2222-2222-2222-222222222222");
        var trailer = Guid.NewGuid();

        await _fx.SeedRankingAsync(matchId, leader, totalPoints: 60);
        await _fx.SeedRankingAsync(matchId, tieHigh, totalPoints: 50);
        await _fx.SeedRankingAsync(matchId, tieLow, totalPoints: 50);
        await _fx.SeedRankingAsync(matchId, trailer, totalPoints: 10);

        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var body = await client.GetFromJsonAsync<GroupRankingResponse>(Url(matchId), Ct);

        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(4);

        // Ordering: leader (60), then tie broken by UserId ascending (tieLow < tieHigh), then trailer.
        body.Items[0].UserId.Should().Be(leader);
        body.Items[1].UserId.Should().Be(tieLow);
        body.Items[2].UserId.Should().Be(tieHigh);
        body.Items[3].UserId.Should().Be(trailer);

        body.Items[1].TotalPoints.Should().Be(50);
        body.Items[2].TotalPoints.Should().Be(50);

        // Absolute rank == (page-1)*pageSize + i + 1 (page 1).
        body.Items.Select(x => x.Rank).Should().Equal(1, 2, 3, 4);
    }

    // ═══ AC-13 / AC-14 / AC-15: pagination + caller entry ══════════════════════

    /// <summary>Covers AC-13 (HasNextPage true): 25 players at page 1, pageSize 20 → 20 items, HasNextPage true.</summary>
    [Fact]
    public async Task GET_ranking_page1_of_25_has_20_items_and_next_page()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring);
        await SeedDescendingAsync(matchId, 25);

        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var body = await client.GetFromJsonAsync<GroupRankingResponse>(Url(matchId, page: 1, pageSize: 20), Ct);

        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(20);
        body.TotalCount.Should().Be(25);
        body.HasNextPage.Should().BeTrue();
        body.Items[0].Rank.Should().Be(1);
        body.Items[19].Rank.Should().Be(20);
    }

    /// <summary>Covers AC-14 (HasNextPage false): the same group at page 2, pageSize 20 → 5 items, HasNextPage false.</summary>
    [Fact]
    public async Task GET_ranking_page2_of_25_has_5_items_and_no_next_page()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring);
        await SeedDescendingAsync(matchId, 25);

        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var body = await client.GetFromJsonAsync<GroupRankingResponse>(Url(matchId, page: 2, pageSize: 20), Ct);

        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(5);
        body.HasNextPage.Should().BeFalse();
        // Absolute rank continues across pages: first item on page 2 is rank 21.
        body.Items[0].Rank.Should().Be(21);
        body.Items[4].Rank.Should().Be(25);
    }

    /// <summary>
    /// Covers AC-15 (caller outside page): the caller ranks 25th in a 25-player group; requesting page 1
    /// pageSize 20 the caller is absent from Items but CallerEntry is populated with Rank 25 and the
    /// caller's correct TotalPoints.
    /// </summary>
    [Fact]
    public async Task GET_ranking_caller_outside_page_returns_caller_entry_with_absolute_rank()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring);
        var ordered = await SeedDescendingAsync(matchId, 25);
        var caller = ordered[^1]; // lowest points → rank 25
        var callerPoints = (25 - 24) * 5; // matches SeedDescendingAsync formula for index 24

        var client = _fx.CreateAuthenticatedClient(caller);
        var body = await client.GetFromJsonAsync<GroupRankingResponse>(Url(matchId, page: 1, pageSize: 20), Ct);

        body.Should().NotBeNull();
        body!.Items.Should().NotContain(x => x.UserId == caller);
        body.CallerEntry.Should().NotBeNull();
        body.CallerEntry!.UserId.Should().Be(caller);
        body.CallerEntry.Rank.Should().Be(25);
        body.CallerEntry.TotalPoints.Should().Be(callerPoints);
    }

    // ═══ AC-16: caller with no points ══════════════════════════════════════════

    /// <summary>
    /// Covers AC-16 (caller with no points): when the caller has no standing row, CallerEntry is null
    /// while Items still returns the ranked players.
    /// </summary>
    [Fact]
    public async Task GET_ranking_caller_with_no_points_returns_null_caller_entry()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring);
        await SeedDescendingAsync(matchId, 3);

        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid()); // caller not in the group
        var body = await client.GetFromJsonAsync<GroupRankingResponse>(Url(matchId), Ct);

        body.Should().NotBeNull();
        body!.Items.Should().HaveCount(3);
        body.CallerEntry.Should().BeNull();
    }

    // ═══ AC-10 / AC-11 / AC-12: validation bounds ══════════════════════════════

    /// <summary>Covers AC-10 (pageSize lower bound): pageSize=0 returns 400.</summary>
    [Fact]
    public async Task GET_ranking_pageSize_zero_returns_400()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring);
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(Url(matchId, page: 1, pageSize: 0), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers AC-11 (pageSize upper bound): pageSize=51 returns 400.</summary>
    [Fact]
    public async Task GET_ranking_pageSize_above_max_returns_400()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring);
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(Url(matchId, page: 1, pageSize: 51), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers AC-12 (page lower bound): page=0 returns 400.</summary>
    [Fact]
    public async Task GET_ranking_page_zero_returns_400()
    {
        await _fx.ResetAsync();
        var matchId = await _fx.SeedMatchAsync(MatchType.Recurring);
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync(Url(matchId, page: 0, pageSize: 20), Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}
