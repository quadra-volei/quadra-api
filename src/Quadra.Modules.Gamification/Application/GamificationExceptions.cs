namespace Quadra.Modules.Gamification.Application;

/// <summary>Thrown when no match exists for the requested ranking <c>matchId</c> (maps to 404).</summary>
public sealed class MatchNotFoundForRankingException : Exception
{
    public MatchNotFoundForRankingException(Guid matchId)
        : base($"No match exists for '{matchId}'.") { }
}

/// <summary>
/// Thrown when the requested match exists but is a OneOff match; group ranking is only defined for
/// recurring matches per SCOPE F2.3 (maps to 409).
/// </summary>
public sealed class RankingNotAvailableForOneOffException : Exception
{
    public RankingNotAvailableForOneOffException()
        : base("Group ranking is only available for recurring matches.") { }
}
