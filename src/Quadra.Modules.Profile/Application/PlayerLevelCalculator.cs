using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Calculates a player's current level: the level earned from recorded matches, never lower than
/// the level the player declared at onboarding.
///
/// Earned levels follow <c>docs/PRODUCT.md</c>: only Beginner and Intermediate are computed.
/// Advanced (vote average undefined) and Elite (needs the global ranking) are deferred, so today
/// a player is Advanced only by declaring it and nobody is Elite.
/// </summary>
public static class PlayerLevelCalculator
{
    public const int IntermediateMinMatches = 10;
    public const int IntermediateMinMvps = 1;

    /// <summary>The level earned from match statistics alone.</summary>
    public static PlayerLevel Calculate(PlayerStats stats)
    {
        ArgumentNullException.ThrowIfNull(stats);

        if (stats.MatchesPlayed >= IntermediateMinMatches && stats.MvpsReceived >= IntermediateMinMvps)
        {
            return PlayerLevel.Intermediate;
        }

        return PlayerLevel.Beginner;
    }

    /// <summary>The current level: earned from stats, with the declared level as the floor.</summary>
    public static PlayerLevel Calculate(PlayerStats stats, PlayerLevel? declaredLevel)
    {
        var earned = Calculate(stats);
        return declaredLevel is { } declared && declared > earned ? declared : earned;
    }
}
