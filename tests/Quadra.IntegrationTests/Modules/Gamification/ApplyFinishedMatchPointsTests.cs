using FluentAssertions;
using Quadra.Modules.Gamification.Abstractions;

namespace Quadra.IntegrationTests.Modules.Gamification;

/// <summary>
/// Integration tests for the Gamification write path (<see cref="IMatchPointsWriter"/>) against a real
/// Postgres. Verifies the SCOPE F2.3 scoring and the recompute-not-increment idempotency contract.
///
/// Coverage per acceptance criterion:
///   - AC-1: attended + won + MVP → total_points = 50 and three ledger rows;
///   - AC-2: attended only → total_points = 10 and one ledger row;
///   - AC-3: won without MVP → total_points = 25 (Attendance + Win);
///   - AC-9: applying the same finished match twice (redelivery) causes no drift.
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
        params FinishedMatchPlayerPoints[] players)
    {
        var matchDateTime = new DateTimeOffset(2026, 7, 1, 20, 0, 0, TimeSpan.Zero);
        return new FinishedMatchPoints(
            MatchId: matchId,
            GroupId: matchId,
            MatchDateTime: matchDateTime,
            MvpPlayerId: mvp,
            Players: players,
            FinishedAt: matchDateTime.AddHours(2));
    }

    /// <summary>
    /// Covers AC-1 (scoring): after applying a finished recurring match where P attended, won, and is
    /// the MVP, P's group_rankings row has total_points = 50 and three ledger rows exist for (P, match).
    /// </summary>
    [Fact]
    public async Task Apply_attended_won_and_mvp_gives_50_points_and_three_ledger_rows()
    {
        await _fx.ResetAsync();
        var matchId = Guid.NewGuid();
        var player = Guid.NewGuid();

        await _fx.ApplyPointsAsync(Build(matchId, mvp: player,
            new FinishedMatchPlayerPoints(player, Won: true)));

        var row = await _fx.GetRankingRowAsync(matchId, player);
        row.Should().NotBeNull();
        row!.TotalPoints.Should().Be(50);
        row.MatchesCounted.Should().Be(1);
        row.LastMatchId.Should().Be(matchId);
        (await _fx.CountTransactionsAsync(matchId, player)).Should().Be(3);
    }

    /// <summary>
    /// Covers AC-2 (attendance-only): a participant who attended but neither won nor was MVP has
    /// total_points = 10 and exactly one Attendance ledger row.
    /// </summary>
    [Fact]
    public async Task Apply_attended_only_gives_10_points_and_one_ledger_row()
    {
        await _fx.ResetAsync();
        var matchId = Guid.NewGuid();
        var player = Guid.NewGuid();

        await _fx.ApplyPointsAsync(Build(matchId, mvp: null,
            new FinishedMatchPlayerPoints(player, Won: false)));

        var row = await _fx.GetRankingRowAsync(matchId, player);
        row.Should().NotBeNull();
        row!.TotalPoints.Should().Be(10);
        (await _fx.CountTransactionsAsync(matchId, player)).Should().Be(1);
    }

    /// <summary>
    /// Covers AC-3 (win without MVP): a winning participant who is not MVP has total_points = 25
    /// (Attendance 10 + Win 15) and two ledger rows.
    /// </summary>
    [Fact]
    public async Task Apply_won_without_mvp_gives_25_points_and_two_ledger_rows()
    {
        await _fx.ResetAsync();
        var matchId = Guid.NewGuid();
        var winner = Guid.NewGuid();
        var mvp = Guid.NewGuid();

        await _fx.ApplyPointsAsync(Build(matchId, mvp: mvp,
            new FinishedMatchPlayerPoints(winner, Won: true),
            new FinishedMatchPlayerPoints(mvp, Won: false)));

        var row = await _fx.GetRankingRowAsync(matchId, winner);
        row.Should().NotBeNull();
        row!.TotalPoints.Should().Be(25);
        (await _fx.CountTransactionsAsync(matchId, winner)).Should().Be(2);
    }

    /// <summary>
    /// Covers AC-9 (idempotency): applying the same finished match twice (redelivery) leaves each
    /// affected player's TotalPoints, MatchesCounted, and ledger row count unchanged versus one delivery.
    /// </summary>
    [Fact]
    public async Task Apply_same_finished_match_twice_causes_no_drift()
    {
        await _fx.ResetAsync();
        var matchId = Guid.NewGuid();
        var winner = Guid.NewGuid();
        var loser = Guid.NewGuid();

        var points = Build(matchId, mvp: winner,
            new FinishedMatchPlayerPoints(winner, Won: true),
            new FinishedMatchPlayerPoints(loser, Won: false));

        await _fx.ApplyPointsAsync(points);
        await _fx.ApplyPointsAsync(points); // redelivery

        var winnerRow = await _fx.GetRankingRowAsync(matchId, winner);
        var loserRow = await _fx.GetRankingRowAsync(matchId, loser);

        winnerRow!.TotalPoints.Should().Be(50); // Attendance + Win + Mvp, not doubled
        winnerRow.MatchesCounted.Should().Be(1);
        loserRow!.TotalPoints.Should().Be(10);
        loserRow.MatchesCounted.Should().Be(1);

        (await _fx.CountTransactionsAsync(matchId, winner)).Should().Be(3);
        (await _fx.CountTransactionsAsync(matchId, loser)).Should().Be(1);
        (await _fx.CountTransactionsAsync(matchId)).Should().Be(4);
    }
}
