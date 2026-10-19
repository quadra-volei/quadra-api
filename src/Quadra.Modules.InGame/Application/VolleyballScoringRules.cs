using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Pure, side-effect-free volleyball scoring rules. Standard win-by-2 with no point cap.
/// A single game is one set to the match's points; best of 3 / 5 is kept for games played
/// before the single game existed.
/// </summary>
public static class VolleyballScoringRules
{
    /// <summary>Points of a single game when the match has none (created before the setting).</summary>
    public const int DefaultGamePoints = 25;

    /// <summary>
    /// Number of sets a team must win to take the game: 1 for SingleGame, 2 for BestOf3, 3 for BestOf5.
    /// </summary>
    public static int SetsToWin(ScoreboardFormat format) => format switch
    {
        ScoreboardFormat.SingleGame => 1,
        ScoreboardFormat.BestOf3 => 2,
        ScoreboardFormat.BestOf5 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>
    /// Whether <paramref name="setNumber"/> is the deciding set of the format
    /// (set 3 in BestOf3, set 5 in BestOf5; never in a single game).
    /// </summary>
    public static bool IsDecidingSet(ScoreboardFormat format, int setNumber) => format switch
    {
        ScoreboardFormat.SingleGame => false,
        ScoreboardFormat.BestOf3 => setNumber == 3,
        ScoreboardFormat.BestOf5 => setNumber == 5,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>
    /// Target points for the set: the game's points in a single game; otherwise 15 for a deciding
    /// set and 25 for the others.
    /// </summary>
    public static int TargetForSet(ScoreboardFormat format, int setNumber, int? pointsToWin = null) =>
        format == ScoreboardFormat.SingleGame
            ? pointsToWin ?? DefaultGamePoints
            : IsDecidingSet(format, setNumber) ? 15 : 25;

    /// <summary>
    /// Whether a set is won given the two point totals and the <paramref name="target"/>:
    /// a team reached the target and leads by at least 2 (no cap).
    /// </summary>
    public static bool IsSetWon(int pointsA, int pointsB, int target) =>
        (pointsA >= target || pointsB >= target) && Math.Abs(pointsA - pointsB) >= 2;
}
