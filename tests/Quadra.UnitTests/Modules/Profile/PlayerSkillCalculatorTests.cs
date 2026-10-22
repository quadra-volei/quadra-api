using FluentAssertions;
using Quadra.Modules.Profile.Application;
using Quadra.Modules.Profile.Entities;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>
/// Unit tests for <see cref="PlayerSkillCalculator"/> and the declared-level floor of
/// <see cref="PlayerLevelCalculator"/>.
///
/// Covers: F2.1 skill ratings — base by declared level (Beginner 50, Intermediate 60,
/// Advanced 70), per-position adjustment, clamped to 1..99; their evolution from recorded actions
/// (DECISIONS #85): rate per standard game on a capped curve, pulled towards the baseline, GERAL
/// weighted by position; and "level — self-declared at onboarding, then recalculated", never
/// below the declared level.
/// </summary>
public sealed class PlayerSkillCalculatorTests
{
    /// <summary>Covers: the base rating of each declarable level (COR has no adjustment).</summary>
    [Theory]
    [InlineData(PlayerLevel.Beginner, 50)]
    [InlineData(PlayerLevel.Intermediate, 60)]
    [InlineData(PlayerLevel.Advanced, 70)]
    public void Base_rating_follows_the_declared_level(PlayerLevel level, int expected)
    {
        PlayerSkillCalculator.Calculate(level, PlayerPosition.COR)
            .Should().Be(new PlayerSkills(expected, expected, expected, expected, expected));
    }

    /// <summary>Covers: a profile with nothing declared yet gets the Beginner base.</summary>
    [Fact]
    public void Nothing_declared_gets_the_beginner_base()
    {
        PlayerSkillCalculator.Calculate(null, null).Should().Be(new PlayerSkills(50, 50, 50, 50, 50));
    }

    /// <summary>
    /// Covers: each position's adjustment on top of the Intermediate base (60), and GERAL before
    /// any stat is the rounded average (the value players had before the formula existed):
    /// CEN BLK+8 ATA+3; LIB DEF+10 ATA-5 BLK-5; PON ATA+6 DEF+3; OPO ATA+8 BLK+3;
    /// LEV DEF+4 ACE+4; COR none.
    /// </summary>
    [Theory]
    [InlineData(PlayerPosition.CEN, 60, 68, 63, 60, 63)]
    [InlineData(PlayerPosition.LIB, 60, 55, 55, 70, 60)]
    [InlineData(PlayerPosition.PON, 60, 60, 66, 63, 62)]
    [InlineData(PlayerPosition.OPO, 60, 63, 68, 60, 63)]
    [InlineData(PlayerPosition.LEV, 64, 60, 60, 64, 62)]
    [InlineData(PlayerPosition.COR, 60, 60, 60, 60, 60)]
    public void Position_adjusts_the_base(PlayerPosition position, int ace, int block, int attack, int defense, int overall)
    {
        PlayerSkillCalculator.Calculate(PlayerLevel.Intermediate, position)
            .Should().Be(new PlayerSkills(ace, block, attack, defense, overall));
    }

    /// <summary>Covers: every rating stays within 1..99 for every level/position combination.</summary>
    [Fact]
    public void Ratings_stay_within_bounds()
    {
        foreach (var level in new PlayerLevel?[] { null, PlayerLevel.Beginner, PlayerLevel.Intermediate, PlayerLevel.Advanced, PlayerLevel.Elite })
        {
            foreach (var position in Enum.GetValues<PlayerPosition>())
            {
                var skills = PlayerSkillCalculator.Calculate(level, position);
                new[] { skills.Ace, skills.Block, skills.Attack, skills.Defense, skills.Overall }
                    .Should().OnlyContain(rating => rating >= 1 && rating <= 99);
            }
        }
    }

    // ─── evolution from recorded actions ─────────────────────────────────────

    /// <summary>An action recorded over <paramref name="games"/> standard games (weight = games).</summary>
    private static ActionTally Per(double perGame, double games) => new((int)Math.Round(perGame * games), 1, games, games);

    private static PlayerActionTotals Sheet(
        double games,
        double? kills = null,
        double? blocks = null,
        double? aces = null,
        double? digs = null,
        double? assists = null)
    {
        ActionTally Tally(double? perGame) => perGame is null ? ActionTally.None : Per(perGame.Value, games);
        return new PlayerActionTotals(1, Tally(kills), Tally(blocks), Tally(aces), Tally(digs), Tally(assists));
    }

    /// <summary>Covers: the curve gives 40 at zero, 75 at the good rate and never passes 99.</summary>
    [Fact]
    public void Performance_curve_has_a_floor_a_good_mark_and_a_ceiling()
    {
        PlayerSkillCalculator.Performance(0, 3).Should().Be(40);
        PlayerSkillCalculator.Performance(3, 3).Should().BeApproximately(75, 0.001);
        PlayerSkillCalculator.Performance(1000, 3).Should().BeLessThanOrEqualTo(99);
    }

    /// <summary>Covers: an action never recorded leaves its rating at the baseline (not read as zero).</summary>
    [Fact]
    public void An_unrecorded_action_keeps_the_baseline()
    {
        var skills = PlayerSkillCalculator.Calculate(PlayerLevel.Beginner, PlayerPosition.COR, Sheet(games: 24, digs: 3));

        skills.Ace.Should().Be(50);
        skills.Block.Should().Be(50);
        skills.Attack.Should().Be(50);
        skills.Defense.Should().BeGreaterThan(50);
    }

    /// <summary>
    /// Covers: one exceptional match (capped at 6 games) moves a rating at most a third of the way
    /// to the ceiling: base 50, ~99 performance → (6 × 99 + 12 × 50) / 18 ≈ 66.
    /// </summary>
    [Fact]
    public void One_exceptional_match_moves_a_rating_at_most_a_third()
    {
        var skills = PlayerSkillCalculator.Calculate(
            PlayerLevel.Beginner, PlayerPosition.COR, Sheet(games: PlayerSkillCalculator.MaxGamesPerMatch, kills: 30));

        skills.Attack.Should().BeInRange(60, 67);
    }

    /// <summary>
    /// Covers: playing more does not raise a rating by itself. With no cortada recorded (→ 40),
    /// more games pull ATK down towards it (47 after 6 games, 42 after 48); at the good rate they
    /// pull it up towards 75.
    /// </summary>
    [Fact]
    public void More_games_only_make_the_rate_weigh_more()
    {
        int Attack(double killsPerGame, double games) =>
            PlayerSkillCalculator.Calculate(PlayerLevel.Beginner, PlayerPosition.COR, Sheet(games, kills: killsPerGame)).Attack;

        Attack(0, games: 6).Should().Be(47);
        Attack(0, games: 48).Should().Be(42);
        Attack(3, games: 48).Should().BeGreaterThan(Attack(3, games: 6));
        Attack(3, games: 1000).Should().Be(75);
    }

    /// <summary>
    /// Covers: assists feed the setter's ATK (half a cortada each). A LEV with 6 assists and 0.5
    /// cortada a game over 24 games: ATK 69; without the assists it would be 49.
    /// </summary>
    [Fact]
    public void Assists_build_the_setters_attack()
    {
        var withAssists = PlayerSkillCalculator.Calculate(PlayerLevel.Beginner, PlayerPosition.LEV, Sheet(24, kills: 0.5, assists: 6));
        var withoutAssists = PlayerSkillCalculator.Calculate(PlayerLevel.Beginner, PlayerPosition.LEV, Sheet(24, kills: 0.5));

        withAssists.Attack.Should().Be(69);
        withoutAssists.Attack.Should().Be(49);
        withAssists.Overall.Should().BeGreaterThan(withoutAssists.Overall);
    }

    /// <summary>
    /// Covers: a libero is not dragged down by attacking and blocking little. The same sheet (3
    /// salvadas a game, nothing else, 24 games) gives a libero GERAL 56 (from 50) and a COR 49.
    /// </summary>
    [Fact]
    public void Overall_weighs_what_matters_for_the_position()
    {
        var sheet = Sheet(24, kills: 0, blocks: 0, aces: 0, digs: 3);

        var libero = PlayerSkillCalculator.Calculate(PlayerLevel.Beginner, PlayerPosition.LIB, sheet);
        var anywhere = PlayerSkillCalculator.Calculate(PlayerLevel.Beginner, PlayerPosition.COR, sheet);

        libero.Should().Be(new PlayerSkills(Ace: 43, Block: 42, Attack: 42, Defense: 70, Overall: 56));
        anywhere.Overall.Should().Be(49);
    }

    /// <summary>
    /// Covers: a match counts as standard games from the rallies played (45 a game) or, with no
    /// sets, 20 minutes of the planned time (60 when not declared); ×3 in 2x2 and ×1.5 in 4x4.
    /// </summary>
    [Theory]
    [InlineData(90, null, "6X6", 2.0)]
    [InlineData(45, null, "2X2", 3.0)]
    [InlineData(45, null, "4X4", 1.5)]
    [InlineData(null, 120, "6X6", 6.0)]
    [InlineData(null, null, null, 3.0)]
    [InlineData(0, 40, "6X6", 2.0)]
    public void Exposure_comes_from_rallies_or_the_planned_time(int? rallies, int? minutes, string? format, double expected)
    {
        PlayerSkillCalculator.ExposureGames(rallies, minutes, format).Should().Be(expected);
    }

    /// <summary>Covers: absurd counts still stay within 1..99.</summary>
    [Fact]
    public void Extreme_sheets_stay_within_bounds()
    {
        var skills = PlayerSkillCalculator.Calculate(
            PlayerLevel.Advanced, PlayerPosition.OPO, Sheet(10_000, kills: 99, blocks: 99, aces: 99, digs: 99, assists: 99));

        new[] { skills.Ace, skills.Block, skills.Attack, skills.Defense, skills.Overall }
            .Should().OnlyContain(rating => rating >= 1 && rating <= 99);
    }

    // ─── level floor ─────────────────────────────────────────────────────────

    private static PlayerStats Stats(int wins, int mvps)
    {
        var stats = PlayerStats.Empty(Guid.NewGuid(), DateTimeOffset.UtcNow);
        stats.Recompute(wins, losses: 0, draws: 0, mvps, DateTimeOffset.UtcNow);
        return stats;
    }

    /// <summary>
    /// Covers: with no recorded matches the level is the declared one; with nothing declared it
    /// is Beginner.
    /// </summary>
    [Theory]
    [InlineData(null, PlayerLevel.Beginner)]
    [InlineData(PlayerLevel.Beginner, PlayerLevel.Beginner)]
    [InlineData(PlayerLevel.Intermediate, PlayerLevel.Intermediate)]
    [InlineData(PlayerLevel.Advanced, PlayerLevel.Advanced)]
    public void New_player_has_the_declared_level(PlayerLevel? declared, PlayerLevel expected)
    {
        PlayerLevelCalculator.Calculate(Stats(wins: 0, mvps: 0), declared).Should().Be(expected);
    }

    /// <summary>
    /// Covers: recalculation raises a declared Beginner to Intermediate once earned (10 matches,
    /// 1 MVP) and never lowers a declared Advanced.
    /// </summary>
    [Fact]
    public void Recalculation_raises_but_never_lowers_the_declared_level()
    {
        var earnedIntermediate = Stats(wins: 10, mvps: 1);

        PlayerLevelCalculator.Calculate(earnedIntermediate, PlayerLevel.Beginner)
            .Should().Be(PlayerLevel.Intermediate);
        PlayerLevelCalculator.Calculate(earnedIntermediate, PlayerLevel.Advanced)
            .Should().Be(PlayerLevel.Advanced);
        PlayerLevelCalculator.Calculate(Stats(wins: 3, mvps: 0), PlayerLevel.Advanced)
            .Should().Be(PlayerLevel.Advanced);
    }
}
