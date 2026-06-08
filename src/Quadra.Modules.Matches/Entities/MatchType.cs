namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Whether the match is a one-off event or a recurring series. Persisted as a string.
/// </summary>
public enum MatchType
{
    Recurring,
    OneOff,
}
