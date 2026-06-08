namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Lifecycle status of a match. Persisted as a string in the database.
/// </summary>
public enum MatchStatus
{
    Draft,
    Open,
    Closed,
    InProgress,
    Ended,
    Cancelled,
}
