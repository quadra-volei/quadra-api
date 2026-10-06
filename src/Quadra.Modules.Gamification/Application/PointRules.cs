using Quadra.Modules.Gamification.Abstractions;
using Quadra.Modules.Gamification.Entities;

namespace Quadra.Modules.Gamification.Application;

/// <summary>
/// Pure point-rule constants and the per-match award mapping (SCOPE F2.3: attendance +10, win +15,
/// MVP +25). No streak or DropIn rule exists in the MVP.
/// </summary>
public static class PointRules
{
    public const int Attendance = 10;
    public const int Win = 15;
    public const int Mvp = 25;

    /// <summary>
    /// Builds the flat list of awards for a finished match: every participant earns Attendance;
    /// winners additionally earn Win; the single MVP additionally earns Mvp.
    /// </summary>
    public static IReadOnlyList<(Guid UserId, PointReason Reason, int Points)> BuildAwards(
        FinishedMatchPoints points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var awards = new List<(Guid UserId, PointReason Reason, int Points)>();

        foreach (var player in points.Players)
        {
            awards.Add((player.PlayerId, PointReason.Attendance, Attendance));

            if (player.Won)
            {
                awards.Add((player.PlayerId, PointReason.Win, Win));
            }

            if (points.MvpPlayerId is { } mvpId && mvpId == player.PlayerId)
            {
                awards.Add((player.PlayerId, PointReason.Mvp, Mvp));
            }
        }

        return awards;
    }
}
