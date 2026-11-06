using Quadra.Modules.Gamification.Abstractions;
using Quadra.Modules.Gamification.Entities;

namespace Quadra.Modules.Gamification.Application;

/// <summary>
/// Pure point rules (DECISIONS #95): attendance +10, +8 per game won (at most 3 counted), +4 per
/// MVP vote received, +20 for the MVP when at least 3 ballots were cast.
/// </summary>
public static class PointRules
{
    public const int Attendance = 10;
    public const int GameWin = 8;
    public const int MaxGamesCounted = 3;
    public const int MvpVote = 4;
    public const int Mvp = 20;
    public const int MvpMinVotes = 3;

    /// <summary>
    /// Builds the flat list of awards for a finished match, one row per player and reason, with the
    /// quantity it stands for (games won, votes received; 1 otherwise).
    /// </summary>
    public static IReadOnlyList<(Guid UserId, PointReason Reason, int Points, int Quantity)> BuildAwards(
        FinishedMatchPoints points)
    {
        ArgumentNullException.ThrowIfNull(points);

        var awards = new List<(Guid UserId, PointReason Reason, int Points, int Quantity)>();
        var mvpCounts = points.MvpTotalVotes >= MvpMinVotes;

        foreach (var player in points.Players)
        {
            awards.Add((player.PlayerId, PointReason.Attendance, Attendance, 1));

            if (player.GamesWon > 0)
            {
                var games = Math.Min(player.GamesWon, MaxGamesCounted);
                awards.Add((player.PlayerId, PointReason.GameWin, games * GameWin, games));
            }

            if (player.MvpVotes > 0)
            {
                awards.Add((player.PlayerId, PointReason.MvpVote, player.MvpVotes * MvpVote, player.MvpVotes));
            }

            if (mvpCounts && points.MvpPlayerId is { } mvpId && mvpId == player.PlayerId)
            {
                awards.Add((player.PlayerId, PointReason.Mvp, Mvp, 1));
            }
        }

        return awards;
    }
}
