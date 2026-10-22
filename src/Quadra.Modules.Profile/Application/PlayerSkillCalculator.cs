using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// A player's four ratings (ACE, BLK, ATK, DEF, 1–99) and GERAL (DECISIONS #85).
///
/// <para><b>Start.</b> Each rating starts at a baseline from what the player declared at
/// onboarding: a base from the level (50 / 60 / 70) plus a per-position adjustment.</para>
///
/// <para><b>Evolution.</b> The organizer records counts per match (cortadas, blocks, aces,
/// salvadas, assistências). Each rating looks at a <i>rate</i>: the count per standard game
/// (6x6, about 45 rallies), over the matches where that action was recorded. The rate becomes a
/// performance value on a curve that starts at 40, gives 75 at a good amateur rate and flattens
/// towards 99, so more actions never push a rating past the ceiling. The rating is the
/// average of that value and the baseline, the baseline weighing as <see cref="PriorGames"/>
/// games: little play moves the rating little, and one match weighs at most
/// <see cref="MaxGamesPerMatch"/> games. Playing more does not raise a rating by itself; it
/// only makes the rate weigh more. A match without a sheet, or an action nobody recorded in
/// it, is left out (never read as zero).</para>
///
/// <para><b>GERAL.</b> The baseline average (the value shown before any stat existed), plus
/// how far each rating moved from its baseline, weighted by what matters for the position: a
/// libero's DEF, a middle's BLK, a setter's ATK, which carries their assists. A setter or a
/// libero is not dragged down by blocking or attacking little.</para>
///
/// Every number lives in this class: it is the single place to recalibrate them.
/// </summary>
public static class PlayerSkillCalculator
{
    public const int MinRating = 1;
    public const int MaxRating = 99;

    /// <summary>The baseline counts as this many standard games of evidence.</summary>
    public const double PriorGames = 12;

    /// <summary>The most one match weighs, in standard games (a third of a fresh player's rating).</summary>
    public const double MaxGamesPerMatch = 6;

    /// <summary>Rallies (points played) in a standard game: a game to 25 lasts about 45.</summary>
    public const double RalliesPerGame = 45;

    /// <summary>Without the games played (no fixed teams), 20 minutes of the planned time make a game.</summary>
    public const double MinutesPerGame = 20;

    public const int DefaultPlannedMinutes = 60;

    /// <summary>An assist counts as half a cortada in ATK: the point starts at the set.</summary>
    public const double AssistWeight = 0.5;

    // Performance curve: rate 0 → Floor, the good rate → GoodRating, approaching the ceiling.
    private const double Floor = 40;
    private const double GoodRating = 75;
    private static readonly double Steepness = Math.Log((MaxRating - Floor) / (MaxRating - GoodRating));

    // What a good amateur does per standard game (6x6, ~45 rallies).
    private const double GoodAttack = 3.0;  // cortadas + 0.5 × assistências
    private const double GoodDefense = 3.0; // salvadas
    private const double GoodBlock = 0.6;
    private const double GoodAce = 0.6;

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

    /// <summary>How much each rating's movement counts in GERAL for the position (they add up to 1).</summary>
    public static (double Ace, double Block, double Attack, double Defense) OverallWeights(PlayerPosition? position) =>
        position switch
        {
            PlayerPosition.PON => (Ace: 0.20, Block: 0.15, Attack: 0.35, Defense: 0.30),
            PlayerPosition.OPO => (Ace: 0.15, Block: 0.25, Attack: 0.45, Defense: 0.15),
            PlayerPosition.CEN => (Ace: 0.15, Block: 0.45, Attack: 0.30, Defense: 0.10),
            PlayerPosition.LEV => (Ace: 0.20, Block: 0.10, Attack: 0.40, Defense: 0.30),
            PlayerPosition.LIB => (Ace: 0.15, Block: 0.05, Attack: 0.05, Defense: 0.75),
            _ => (Ace: 0.25, Block: 0.25, Attack: 0.25, Defense: 0.25),
        };

    /// <summary>The baseline ratings, before any recorded action.</summary>
    public static PlayerSkills Calculate(PlayerLevel? declaredLevel, PlayerPosition? position) =>
        Calculate(declaredLevel, position, PlayerActionTotals.None);

    public static PlayerSkills Calculate(PlayerLevel? declaredLevel, PlayerPosition? position, PlayerActionTotals actions)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var baseRating = BaseRating(declaredLevel);
        var adjustment = PositionAdjustment(position);
        var baseline = (
            Ace: Clamp(baseRating + adjustment.Ace),
            Block: Clamp(baseRating + adjustment.Block),
            Attack: Clamp(baseRating + adjustment.Attack),
            Defense: Clamp(baseRating + adjustment.Defense));

        var attackRate = actions.Kills.PerGame is null && actions.Assists.PerGame is null
            ? (double?)null
            : (actions.Kills.PerGame ?? 0) + (AssistWeight * (actions.Assists.PerGame ?? 0));
        var ace = Rating(baseline.Ace, actions.Aces.PerGame, GoodAce, actions.Aces.Weight);
        var block = Rating(baseline.Block, actions.Blocks.PerGame, GoodBlock, actions.Blocks.Weight);
        var attack = Rating(baseline.Attack, attackRate, GoodAttack, Math.Max(actions.Kills.Weight, actions.Assists.Weight));
        var defense = Rating(baseline.Defense, actions.Digs.PerGame, GoodDefense, actions.Digs.Weight);

        var w = OverallWeights(position);
        var overall = ((baseline.Ace + baseline.Block + baseline.Attack + baseline.Defense) / 4.0)
            + (w.Ace * (ace - baseline.Ace))
            + (w.Block * (block - baseline.Block))
            + (w.Attack * (attack - baseline.Attack))
            + (w.Defense * (defense - baseline.Defense));

        return new PlayerSkills(ace, block, attack, defense, Clamp(Round(overall)));
    }

    /// <summary>
    /// How many standard games a match counts for one player: the rallies they were on court
    /// for, or (no fixed teams) the planned time; times 3 in 2x2 and 1.5 in 4x4, where each
    /// player touches the ball that much more than in 6x6.
    /// </summary>
    public static double ExposureGames(int? ralliesPlayed, int? plannedMinutes, string? format)
    {
        var games = ralliesPlayed is > 0
            ? ralliesPlayed.Value / RalliesPerGame
            : (plannedMinutes is > 0 ? plannedMinutes.Value : DefaultPlannedMinutes) / MinutesPerGame;
        var touches = format switch
        {
            "2X2" => 3.0,
            "4X4" => 1.5,
            _ => 1.0,
        };
        return Math.Round(games * touches, 2);
    }

    /// <summary>The performance value of a rate: 40 at zero, 75 at the good rate, never above 99.</summary>
    public static double Performance(double rate, double goodRate) =>
        Floor + ((MaxRating - Floor) * (1 - Math.Exp(-Steepness * rate / goodRate)));

    private static int Rating(int baseline, double? rate, double goodRate, double weight)
    {
        if (rate is null || weight <= 0)
        {
            return baseline;
        }

        var value = ((weight * Performance(rate.Value, goodRate)) + (PriorGames * baseline)) / (weight + PriorGames);
        return Clamp(Round(value));
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    private static int Clamp(int rating) => Math.Clamp(rating, MinRating, MaxRating);
}
