namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// The outcome of a single finished match from one player's perspective. Persisted as its string
/// name. <c>Draw</c> is reachable because F1.6 permits a null-winner (level) match.
/// </summary>
public enum MatchOutcome
{
    Win,
    Loss,
    Draw,
}
