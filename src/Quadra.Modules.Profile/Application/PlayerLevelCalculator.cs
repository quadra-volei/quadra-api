using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Pure function deriving a player's <see cref="PlayerLevel"/> from their aggregated stats.
/// <b>MVP: two tiers only.</b>
/// <list type="bullet">
///   <item><c>Beginner</c> when <c>matches_played &lt; 10</c>.</item>
///   <item><c>Intermediate</c> when <c>matches_played &gt;= 10 &amp;&amp; mvps_received &gt;= 1</c>.</item>
/// </list>
/// <c>Advanced</c>/<c>Elite</c> are deferred; a player above those thresholds stays
/// <see cref="PlayerLevel.Intermediate"/>.
/// </summary>
public static class PlayerLevelCalculator
{
    public static PlayerLevel Calculate(PlayerStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        if (stats.MatchesPlayed >= 10 && stats.MvpsReceived >= 1)
        {
            return PlayerLevel.Intermediate;
        }

        return PlayerLevel.Beginner;
    }
}
