namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Recurrence frequency for recurring matches. Persisted as a string.
/// Only applicable when <see cref="MatchType"/> is <see cref="MatchType.Recurring"/>.
/// </summary>
public enum MatchFrequency
{
    Weekly,
    Biweekly,
    Monthly,
}
