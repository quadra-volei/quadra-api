using FluentAssertions;
using Quadra.Modules.Profile.Application;
using Quadra.Modules.Profile.Entities;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>
/// Unit tests for <see cref="PlayerSkillCalculator"/> and the declared-level floor of
/// <see cref="PlayerLevelCalculator"/>.
///
/// Covers: F2.1 skill ratings — base by declared level (Beginner 50, Intermediate 60,
/// Advanced 70), per-position adjustment, clamped to 1..99, GERAL = average of the four; and
/// "level — self-declared at onboarding, then recalculated", never below the declared level.
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
        var skills = PlayerSkillCalculator.Calculate(level, PlayerPosition.COR);

        skills.Should().Be(new PlayerSkills(expected, expected, expected, expected));
        skills.Overall.Should().Be(expected);
    }

    /// <summary>Covers: a profile with nothing declared yet gets the Beginner base.</summary>
    [Fact]
    public void Nothing_declared_gets_the_beginner_base()
    {
        PlayerSkillCalculator.Calculate(null, null).Should().Be(new PlayerSkills(50, 50, 50, 50));
    }

    /// <summary>
    /// Covers: each position's adjustment on top of the Intermediate base (60):
    /// CEN BLK+8 ATA+3; LIB DEF+10 ATA-5 BLK-5; PON ATA+6 DEF+3; OPO ATA+8 BLK+3;
    /// LEV DEF+4 ACE+4; COR none.
    /// </summary>
    [Theory]
    [InlineData(PlayerPosition.CEN, 60, 68, 63, 60)]
    [InlineData(PlayerPosition.LIB, 60, 55, 55, 70)]
    [InlineData(PlayerPosition.PON, 60, 60, 66, 63)]
    [InlineData(PlayerPosition.OPO, 60, 63, 68, 60)]
    [InlineData(PlayerPosition.LEV, 64, 60, 60, 64)]
    [InlineData(PlayerPosition.COR, 60, 60, 60, 60)]
    public void Position_adjusts_the_base(PlayerPosition position, int ace, int block, int attack, int defense)
    {
        PlayerSkillCalculator.Calculate(PlayerLevel.Intermediate, position)
            .Should().Be(new PlayerSkills(ace, block, attack, defense));
    }

    /// <summary>Covers: GERAL is the rounded average of the four ratings.</summary>
    [Fact]
    public void Overall_is_the_rounded_average()
    {
        new PlayerSkills(Ace: 64, Block: 60, Attack: 60, Defense: 64).Overall.Should().Be(62);
        new PlayerSkills(Ace: 50, Block: 50, Attack: 50, Defense: 51).Overall.Should().Be(50); // 50.25
        new PlayerSkills(Ace: 50, Block: 51, Attack: 51, Defense: 50).Overall.Should().Be(51); // 50.5 rounds up
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
