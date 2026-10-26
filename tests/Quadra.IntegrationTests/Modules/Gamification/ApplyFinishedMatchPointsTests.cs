using FluentAssertions;
using Quadra.Modules.Gamification.Abstractions;

namespace Quadra.IntegrationTests.Modules.Gamification;

/// <summary>
/// Integration tests for the Gamification write path (<see cref="IMatchPointsWriter"/>) against a real
/// Postgres: the DECISIONS #95 scoring and the recompute-not-increment idempotency contract.
/// </summary>
[Collection("Gamification")]
public sealed class ApplyFinishedMatchPointsTests
{
    private readonly GamificationWebApplicationFactory _fx;

    public ApplyFinishedMatchPointsTests(GamificationWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static FinishedMatchPoints Build(
        Guid matchId,
        Guid? mvp,
        int totalVotes,
        params FinishedMatchPlayerPoints[] players)
    {
        var matchDateTime = new DateTimeOffset(2026, 7, 1, 20, 0, 0, TimeSpan.Zero);
        return new FinishedMatchPoints(matchId, matchId, matchDateTime, mvp, players, matchDateTime.AddHours(2), totalVotes);
    }

    [Fact]
    public async Task Apply_mvp_with_games_and_votes_gives_all_four_ledger_rows()
    {
        await _fx.ResetAsync();
        var matchId = Guid.NewGuid();
        var player = Guid.NewGuid();

        await _fx.ApplyPointsAsync(Build(matchId, player, 6,
            new FinishedMatchPlayerPoints(player, GamesWon: 2, MvpVotes: 5)));

        var row = await _fx.GetRankingRowAsync(matchId, player);
        row!.TotalPoints.Should().Be(66);
        row.MatchesCounted.Should().Be(1);
        row.LastMatchId.Should().Be(matchId);
        (await _fx.CountTransactionsAsync(matchId, player)).Should().Be(4);
    }

    [Fact]
    public async Task Apply_attended_only_gives_10_points_and_one_ledger_row()
    {
        await _fx.ResetAsync();
        var matchId = Guid.NewGuid();
        var player = Guid.NewGuid();

        await _fx.ApplyPointsAsync(Build(matchId, null, 0, new FinishedMatchPlayerPoints(player)));

        (await _fx.GetRankingRowAsync(matchId, player))!.TotalPoints.Should().Be(10);
        (await _fx.CountTransactionsAsync(matchId, player)).Should().Be(1);
    }

    [Fact]
    public async Task Apply_same_finished_match_twice_causes_no_drift()
    {
        await _fx.ResetAsync();
        var matchId = Guid.NewGuid();
        var winner = Guid.NewGuid();
        var loser = Guid.NewGuid();

        var points = Build(matchId, winner, 3,
            new FinishedMatchPlayerPoints(winner, GamesWon: 1, MvpVotes: 2),
            new FinishedMatchPlayerPoints(loser, MvpVotes: 1));

        await _fx.ApplyPointsAsync(points);
        await _fx.ApplyPointsAsync(points); // redelivery

        (await _fx.GetRankingRowAsync(matchId, winner))!.TotalPoints.Should().Be(46); // 10 + 8 + 8 + 20
        (await _fx.GetRankingRowAsync(matchId, loser))!.TotalPoints.Should().Be(14);
        (await _fx.CountTransactionsAsync(matchId)).Should().Be(6);
    }
}
