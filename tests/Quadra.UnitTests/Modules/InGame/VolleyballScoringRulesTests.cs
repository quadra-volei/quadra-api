using FluentAssertions;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Entities;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="VolleyballScoringRules"/> — the pure, side-effect-free scoring rules.
///
/// Covers (F1.4 acceptance criterion "set-by-set scoreboard, best of 3 or 5"):
///   - TargetForSet: 25 for a regular set, 15 for the deciding set.
///   - IsDecidingSet: set 3 in BestOf3, set 5 in BestOf5.
///   - SetsToWin: 2 for BestOf3, 3 for BestOf5.
///   - IsSetWon: reach target with a win-by-2 margin, no point cap.
/// </summary>
public sealed class VolleyballScoringRulesTests
{
    // ─── SetsToWin ───────────────────────────────────────────────────────────

    /// <summary>Covers: F1.4 — a team must win 2 sets to take a BestOf3 match.</summary>
    [Fact]
    public void SetsToWin_for_BestOf3_is_2()
    {
        VolleyballScoringRules.SetsToWin(ScoreboardFormat.BestOf3).Should().Be(2);
    }

    /// <summary>Covers: F1.4 — a team must win 3 sets to take a BestOf5 match.</summary>
    [Fact]
    public void SetsToWin_for_BestOf5_is_3()
    {
        VolleyballScoringRules.SetsToWin(ScoreboardFormat.BestOf5).Should().Be(3);
    }

    // ─── IsDecidingSet ───────────────────────────────────────────────────────

    /// <summary>Covers: F1.4 — set 3 is the deciding set in a BestOf3 match.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, true)]
    public void IsDecidingSet_BestOf3_only_set_3_is_deciding(int setNumber, bool expected)
    {
        VolleyballScoringRules.IsDecidingSet(ScoreboardFormat.BestOf3, setNumber)
            .Should().Be(expected);
    }

    /// <summary>Covers: F1.4 — set 5 is the deciding set in a BestOf5 match.</summary>
    [Theory]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(3, false)]
    [InlineData(4, false)]
    [InlineData(5, true)]
    public void IsDecidingSet_BestOf5_only_set_5_is_deciding(int setNumber, bool expected)
    {
        VolleyballScoringRules.IsDecidingSet(ScoreboardFormat.BestOf5, setNumber)
            .Should().Be(expected);
    }

    // ─── TargetForSet ────────────────────────────────────────────────────────

    /// <summary>Covers: F1.4 — a regular set targets 25 points.</summary>
    [Theory]
    [InlineData(ScoreboardFormat.BestOf3, 1, 25)]
    [InlineData(ScoreboardFormat.BestOf3, 2, 25)]
    [InlineData(ScoreboardFormat.BestOf5, 1, 25)]
    [InlineData(ScoreboardFormat.BestOf5, 4, 25)]
    public void TargetForSet_regular_set_is_25(ScoreboardFormat format, int setNumber, int expected)
    {
        VolleyballScoringRules.TargetForSet(format, setNumber).Should().Be(expected);
    }

    /// <summary>Covers: F1.4 — the deciding set targets 15 points.</summary>
    [Theory]
    [InlineData(ScoreboardFormat.BestOf3, 3, 15)]
    [InlineData(ScoreboardFormat.BestOf5, 5, 15)]
    public void TargetForSet_deciding_set_is_15(ScoreboardFormat format, int setNumber, int expected)
    {
        VolleyballScoringRules.TargetForSet(format, setNumber).Should().Be(expected);
    }

    // ─── IsSetWon: not yet reached target ────────────────────────────────────

    /// <summary>Covers: F1.4 — a set is not won before either team reaches the target.</summary>
    [Theory]
    [InlineData(24, 20, 25)] // leader below 25
    [InlineData(0, 0, 25)]
    [InlineData(14, 10, 15)] // deciding set: leader below 15
    public void IsSetWon_returns_false_when_target_not_reached(int a, int b, int target)
    {
        VolleyballScoringRules.IsSetWon(a, b, target).Should().BeFalse();
    }

    // ─── IsSetWon: target reached but no 2-point margin ──────────────────────

    /// <summary>Covers: F1.4 — win-by-2: reaching the target with a 1-point lead does not win.</summary>
    [Theory]
    [InlineData(25, 24, 25)]
    [InlineData(30, 29, 25)] // no cap: play continues past 25 while margin < 2
    [InlineData(15, 14, 15)] // deciding set
    public void IsSetWon_returns_false_when_margin_below_two(int a, int b, int target)
    {
        VolleyballScoringRules.IsSetWon(a, b, target).Should().BeFalse();
    }

    // ─── IsSetWon: won ───────────────────────────────────────────────────────

    /// <summary>Covers: F1.4 — a set is won when the target is reached with a 2+ point margin (no cap).</summary>
    [Theory]
    [InlineData(25, 23, 25)] // exact 2-point win
    [InlineData(25, 0, 25)]  // shutout
    [InlineData(27, 25, 25)] // extended set, win by 2
    [InlineData(31, 29, 25)] // deep extended set, no cap
    [InlineData(15, 13, 15)] // deciding set win by 2
    [InlineData(23, 25, 25)] // team B wins
    public void IsSetWon_returns_true_when_target_reached_with_margin(int a, int b, int target)
    {
        VolleyballScoringRules.IsSetWon(a, b, target).Should().BeTrue();
    }
}
