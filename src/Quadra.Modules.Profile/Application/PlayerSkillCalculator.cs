using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Derives a player's starting skill ratings from what they declared at onboarding: a base value
/// from the declared level, plus an adjustment for the declared position, clamped to 1–99.
///
/// Every number lives in this class — it is the single place to recalibrate them. They are
/// first-pass defaults agreed with the product owner, not a tuned model. Ratings do not yet
/// evolve with play: growth from recorded events (attendance, wins, MVPs, streaks) is a planned
/// follow-up and will be added here.
/// </summary>
public static class PlayerSkillCalculator
{
    public const int MinRating = 1;
    public const int MaxRating = 99;

    /// <summary>Base of all four ratings for each level a player can declare.</summary>
    private static int BaseRating(PlayerLevel? declaredLevel) => declaredLevel switch
    {
        PlayerLevel.Intermediate => 60,
        PlayerLevel.Advanced => 70,
        // Beginner, and a player who has not declared a level yet.
        _ => 50,
    };

    /// <summary>Per-position adjustment added to the base (ace, block, attack, defense).</summary>
    private static (int Ace, int Block, int Attack, int Defense) PositionAdjustment(PlayerPosition? position) =>
        position switch
        {
            PlayerPosition.CEN => (Ace: 0, Block: 8, Attack: 3, Defense: 0),
            PlayerPosition.LIB => (Ace: 0, Block: -5, Attack: -5, Defense: 10),
            PlayerPosition.PON => (Ace: 0, Block: 0, Attack: 6, Defense: 3),
            PlayerPosition.OPO => (Ace: 0, Block: 3, Attack: 8, Defense: 0),
            PlayerPosition.LEV => (Ace: 4, Block: 0, Attack: 0, Defense: 4),
            // COR (plays anywhere) and no position: no adjustment.
            _ => (Ace: 0, Block: 0, Attack: 0, Defense: 0),
        };

    public static PlayerSkills Calculate(PlayerLevel? declaredLevel, PlayerPosition? position)
    {
        var baseRating = BaseRating(declaredLevel);
        var adjustment = PositionAdjustment(position);

        return new PlayerSkills(
            Clamp(baseRating + adjustment.Ace),
            Clamp(baseRating + adjustment.Block),
            Clamp(baseRating + adjustment.Attack),
            Clamp(baseRating + adjustment.Defense));
    }

    private static int Clamp(int rating) => Math.Clamp(rating, MinRating, MaxRating);
}
