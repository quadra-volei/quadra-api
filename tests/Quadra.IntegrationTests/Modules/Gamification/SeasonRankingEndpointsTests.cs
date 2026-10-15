using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Quadra.Modules.Gamification.Abstractions;
using Quadra.Modules.Gamification.Contracts;
using Quadra.Modules.Matches.Entities;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.IntegrationTests.Modules.Gamification;

/// <summary><c>GET /api/v1/rankings</c> and <c>GET /api/v1/rankings/groups</c> (DECISIONS #63).</summary>
[Collection("Gamification")]
public sealed class SeasonRankingEndpointsTests
{
    private readonly GamificationWebApplicationFactory _fx;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SeasonRankingEndpointsTests(GamificationWebApplicationFactory fx)
    {
        _fx = fx;
    }

    /// <summary>One finished match of <paramref name="groupId"/> played today, every player losing.</summary>
    private Task PlayAsync(Guid groupId, Guid? mvp, params Guid[] players) =>
        _fx.ApplyPointsAsync(new FinishedMatchPoints(
            MatchId: Guid.NewGuid(),
            GroupId: groupId,
            MatchDateTime: DateTimeOffset.UtcNow,
            MvpPlayerId: mvp,
            Players: players.Select(p => new FinishedMatchPlayerPoints(p, Won: false)).ToList(),
            FinishedAt: DateTimeOffset.UtcNow));

    [Fact]
    public async Task General_lists_only_players_with_three_games_and_tells_the_caller_where_they_stand()
    {
        await _fx.ResetAsync();
        var regular = Guid.NewGuid();
        var newcomer = Guid.NewGuid();
        var group = Guid.NewGuid();
        await PlayAsync(group, newcomer, regular, newcomer);
        await PlayAsync(group, null, regular);
        await PlayAsync(group, null, regular);

        var body = await _fx.CreateAuthenticatedClient(newcomer)
            .GetFromJsonAsync<RankingResponse>("/api/v1/rankings?scope=general", Ct);

        body!.MinMatches.Should().Be(3);
        body.Items.Select(i => (i.UserId, i.Rank, i.Points, i.Matches)).Should().Equal((regular, 1, 30, 3));
        body.CallerEntry.Should().BeEquivalentTo(new { UserId = newcomer, Rank = (int?)null, Points = 35, Matches = 1, Mvps = 1 });
    }

    [Fact]
    public async Task Friends_counts_the_caller_and_the_players_they_follow_only()
    {
        await _fx.ResetAsync();
        var me = Guid.NewGuid();
        var friend = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        await _fx.SeedFollowAsync(me, friend);
        await PlayAsync(Guid.NewGuid(), friend, me, friend, stranger);

        var body = await _fx.CreateAuthenticatedClient(me)
            .GetFromJsonAsync<RankingResponse>("/api/v1/rankings?scope=friends", Ct);

        body!.Items.Select(i => (i.UserId, i.Rank)).Should().Equal((friend, 1), (me, 2));
        body.CallerEntry!.Rank.Should().Be(2);
    }

    [Fact]
    public async Task Group_counts_only_that_group_and_lists_in_the_picker()
    {
        await _fx.ResetAsync();
        var me = Guid.NewGuid();
        var group = await _fx.SeedMatchAsync(MatchType.Recurring, "Terça do vôlei");
        await PlayAsync(group, null, me);
        await PlayAsync(Guid.NewGuid(), me, me);
        var client = _fx.CreateAuthenticatedClient(me);

        var body = await client.GetFromJsonAsync<RankingResponse>($"/api/v1/rankings?scope=group&groupId={group}", Ct);
        var groups = await client.GetFromJsonAsync<List<RankingGroupResponse>>("/api/v1/rankings/groups", Ct);

        body!.GroupName.Should().Be("Terça do vôlei");
        body.Items.Should().ContainSingle().Which.Points.Should().Be(10);
        groups.Should().ContainSingle().Which.Should().Be(new RankingGroupResponse(group, "Terça do vôlei"));
    }

    [Fact]
    public async Task Group_the_caller_may_not_see_is_404()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());

        var response = await client.GetAsync($"/api/v1/rankings?scope=group&groupId={Guid.NewGuid()}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("scope=everyone")]
    [InlineData("scope=group")]
    [InlineData("scope=general&position=XYZ")]
    [InlineData("scope=general&pageSize=51")]
    public async Task Bad_query_is_400(string query)
    {
        var response = await _fx.CreateAuthenticatedClient(Guid.NewGuid()).GetAsync($"/api/v1/rankings?{query}", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Without_token_is_401()
    {
        var response = await _fx.CreateAnonymousClient().GetAsync("/api/v1/rankings", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
