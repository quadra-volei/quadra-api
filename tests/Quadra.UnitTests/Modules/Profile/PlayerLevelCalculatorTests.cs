using FluentAssertions;
using Quadra.Modules.Profile.Application;
using Quadra.Modules.Profile.Entities;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>
/// Unit tests for <see cref="PlayerLevelCalculator"/> — the two-tier MVP level rule.
///
/// Covers: F2.1 AC "automatically calculated level (MVP scope: Beginner and Intermediate only)":
///   - Beginner when matches_played &lt; 10;
///   - Intermediate when matches_played &gt;= 10 &amp;&amp; mvps_received &gt;= 1;
///   - a player above the (deferred) Advanced/Elite thresholds stays Intermediate.
/// </summary>
public sealed class PlayerLevelCalculatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid PlayerId = new("11111111-1111-1111-1111-111111111111");

    /// <summary>Builds a stats row with the given outcome/mvp totals (matches = wins+losses+draws).</summary>
    private static PlayerStats Stats(int wins, int losses, int draws, int mvps)
    {
        var stats = PlayerStats.Empty(PlayerId, Now);
        stats.Recompute(wins, losses, draws, mvps, Now);
        return stats;
    }

    /// <summary>Covers: a brand-new player (0 matches) is Beginner.</summary>
    [Fact]
    public void Calculate_with_zero_matches_returns_Beginner()
    {
        PlayerLevelCalculator.Calculate(Stats(0, 0, 0, 0))
            .Should().Be(PlayerLevel.Beginner);
    }

    /// <summary>Covers: boundary — 9 matches (one below the threshold) stays Beginner even with an MVP.</summary>
    [Fact]
    public void Calculate_with_nine_matches_returns_Beginner()
    {
        PlayerLevelCalculator.Calculate(Stats(8, 0, 1, 1))
            .Should().Be(PlayerLevel.Beginner);
    }

    /// <summary>Covers: boundary — 10 matches but 0 MVPs is still Beginner (both conditions required).</summary>
    [Fact]
    public void Calculate_with_ten_matches_and_zero_mvps_returns_Beginner()
    {
        PlayerLevelCalculator.Calculate(Stats(10, 0, 0, 0))
            .Should().Be(PlayerLevel.Beginner);
    }

    /// <summary>Covers: boundary — 10 matches AND 1 MVP is the first Intermediate case.</summary>
    [Fact]
    public void Calculate_with_ten_matches_and_one_mvp_returns_Intermediate()
    {
        PlayerLevelCalculator.Calculate(Stats(9, 0, 1, 1))
            .Should().Be(PlayerLevel.Intermediate);
    }

    /// <summary>Covers: a player well above the deferred Advanced/Elite thresholds stays Intermediate.</summary>
    [Fact]
    public void Calculate_far_above_advanced_thresholds_stays_Intermediate()
    {
        PlayerLevelCalculator.Calculate(Stats(120, 30, 10, 75))
            .Should().Be(PlayerLevel.Intermediate);
    }

    /// <summary>Covers: draws count toward matches_played for the threshold.</summary>
    [Fact]
    public void Calculate_counts_draws_toward_matches_played()
    {
        // 5 wins + 4 losses + 1 draw = 10 matches, 1 mvp → Intermediate.
        PlayerLevelCalculator.Calculate(Stats(5, 4, 1, 1))
            .Should().Be(PlayerLevel.Intermediate);
    }

    /// <summary>Covers: null stats guard.</summary>
    [Fact]
    public void Calculate_with_null_stats_throws()
    {
        var act = () => PlayerLevelCalculator.Calculate(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
