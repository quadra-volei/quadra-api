using FluentAssertions;
using Quadra.Modules.Gamification.Abstractions;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Entities;

namespace Quadra.UnitTests.Modules.Gamification;

/// <summary>
/// Unit tests for <see cref="PointRules"/> — the pure per-match award mapping
/// (SCOPE F2.3: attendance +10, win +15, MVP +25). These cover the scoring rules that
/// AC-1/AC-2/AC-3 assert on; the full DB recompute is additionally verified against real Postgres
/// in <c>ApplyFinishedMatchPointsTests</c>.
/// </summary>
public sealed class PointRulesTests
{
    private static readonly Guid Winner = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Loser = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MatchId = new("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset MatchDateTime = new(2026, 7, 1, 20, 0, 0, TimeSpan.Zero);

    private static FinishedMatchPoints Build(
        Guid? mvp,
        params FinishedMatchPlayerPoints[] players) =>
        new(
            MatchId: MatchId,
            GroupId: MatchId,
            MatchDateTime: MatchDateTime,
            MvpPlayerId: mvp,
            Players: players,
            FinishedAt: MatchDateTime.AddHours(2));

    /// <summary>
    /// Covers AC-1 (scoring): a player who attended, won, and is MVP earns three awards summing to 50
    /// (Attendance 10 + Win 15 + Mvp 25).
    /// </summary>
    [Fact]
    public void BuildAwards_attended_won_and_mvp_yields_attendance_win_and_mvp()
    {
        var points = Build(mvp: Winner, new FinishedMatchPlayerPoints(Winner, Won: true));

        var awards = PointRules.BuildAwards(points);

        awards.Should().BeEquivalentTo(new[]
        {
            (Winner, PointReason.Attendance, 10),
            (Winner, PointReason.Win, 15),
            (Winner, PointReason.Mvp, 25),
        });
        awards.Where(a => a.UserId == Winner).Sum(a => a.Points).Should().Be(50);
    }

    /// <summary>
    /// Covers AC-2 (attendance-only): a participant who attended but neither won nor was MVP earns a
    /// single Attendance award of 10.
    /// </summary>
    [Fact]
    public void BuildAwards_attended_only_yields_single_attendance()
    {
        var points = Build(mvp: null, new FinishedMatchPlayerPoints(Loser, Won: false));

        var awards = PointRules.BuildAwards(points);

        awards.Should().ContainSingle()
            .Which.Should().Be((Loser, PointReason.Attendance, 10));
    }

    /// <summary>
    /// Covers AC-3 (win without MVP): a winning participant who is not the MVP earns Attendance + Win
    /// = 25, and no Mvp award.
    /// </summary>
    [Fact]
    public void BuildAwards_won_without_mvp_yields_attendance_and_win_only()
    {
        var points = Build(
            mvp: Loser, // MVP is someone else in the match
            new FinishedMatchPlayerPoints(Winner, Won: true),
            new FinishedMatchPlayerPoints(Loser, Won: false));

        var awards = PointRules.BuildAwards(points);

        var winnerAwards = awards.Where(a => a.UserId == Winner).ToList();
        winnerAwards.Should().BeEquivalentTo(new[]
        {
            (Winner, PointReason.Attendance, 10),
            (Winner, PointReason.Win, 15),
        });
        winnerAwards.Sum(a => a.Points).Should().Be(25);
        winnerAwards.Should().NotContain(a => a.Reason == PointReason.Mvp);
    }

    /// <summary>
    /// Supports AC-1: only the single player whose id equals <c>MvpPlayerId</c> receives the Mvp award;
    /// a null MvpPlayerId produces no Mvp award for anyone.
    /// </summary>
    [Fact]
    public void BuildAwards_mvp_awarded_to_exactly_one_player_and_none_when_null()
    {
        var withMvp = PointRules.BuildAwards(Build(
            mvp: Winner,
            new FinishedMatchPlayerPoints(Winner, Won: true),
            new FinishedMatchPlayerPoints(Loser, Won: false)));

        withMvp.Count(a => a.Reason == PointReason.Mvp).Should().Be(1);
        withMvp.Single(a => a.Reason == PointReason.Mvp).UserId.Should().Be(Winner);

        var noMvp = PointRules.BuildAwards(Build(
            mvp: null,
            new FinishedMatchPlayerPoints(Winner, Won: true),
            new FinishedMatchPlayerPoints(Loser, Won: false)));

        noMvp.Should().NotContain(a => a.Reason == PointReason.Mvp);
    }

    /// <summary>Point-rule constants match the SCOPE F2.3 values.</summary>
    [Fact]
    public void PointRule_constants_match_scope()
    {
        PointRules.Attendance.Should().Be(10);
        PointRules.Win.Should().Be(15);
        PointRules.Mvp.Should().Be(25);
    }
}
