using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Pure, side-effect-free volleyball scoring rules for best-of-N matches.
/// Standard win-by-2 with no point cap.
/// </summary>
public static class VolleyballScoringRules
{
    /// <summary>
    /// Number of sets a team must win to take the match: 2 for BestOf3, 3 for BestOf5.
    /// </summary>
    public static int SetsToWin(ScoreboardFormat format) => format switch
    {
        ScoreboardFormat.BestOf3 => 2,
        ScoreboardFormat.BestOf5 => 3,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>
    /// Whether <paramref name="setNumber"/> is the deciding set of the format
    /// (set 3 in BestOf3, set 5 in BestOf5).
    /// </summary>
    public static bool IsDecidingSet(ScoreboardFormat format, int setNumber) => format switch
    {
        ScoreboardFormat.BestOf3 => setNumber == 3,
        ScoreboardFormat.BestOf5 => setNumber == 5,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    /// <summary>
    /// Target points for the set: 15 for a deciding set, otherwise 25.
    /// </summary>
    public static int TargetForSet(ScoreboardFormat format, int setNumber) =>
        IsDecidingSet(format, setNumber) ? 15 : 25;

    /// <summary>
    /// Whether a set is won given the two point totals and the <paramref name="target"/>:
    /// a team reached the target and leads by at least 2 (no cap).
    /// </summary>
    public static bool IsSetWon(int pointsA, int pointsB, int target) =>
        (pointsA >= target || pointsB >= target) && Math.Abs(pointsA - pointsB) >= 2;
}
