using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// A player's accumulated points and level number (docs/PRODUCT.md). Points are derived from the
/// stats, which are themselves recomputed from the match history, so they are never stored:
/// attendance +10 per finished match, win +15, MVP +25. Reaching level n takes
/// 10 × n × (n − 1) points (level 1 = 0, level 2 = 20, level 5 = 200, level 16 = 2400).
/// The Beginner…Elite band (<see cref="PlayerLevel"/>) is separate and unchanged.
/// </summary>
public static class PlayerProgression
{
    public const int AttendancePoints = 10;
    public const int WinPoints = 15;
    public const int MvpPoints = 25;

    public static int PointsOf(PlayerStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);
        return PointsOf(stats.MatchesPlayed, stats.Wins, stats.MvpsReceived);
    }

    public static int PointsOf(int matchesPlayed, int wins, int mvpsReceived) =>
        (AttendancePoints * matchesPlayed) + (WinPoints * wins) + (MvpPoints * mvpsReceived);

    /// <summary>Points needed to reach <paramref name="level"/>.</summary>
    public static int PointsForLevel(int level) => 10 * level * (level - 1);

    public static int LevelFor(int points)
    {
        var level = 1;
        while (PointsForLevel(level + 1) <= points)
        {
            level++;
        }

        return level;
    }

    public static int LevelOf(PlayerStats stats) => LevelFor(PointsOf(stats));
}
