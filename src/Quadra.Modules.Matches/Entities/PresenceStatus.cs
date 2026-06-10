namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Status of a player's presence in a match. Persisted as a string in the database.
/// </summary>
public enum PresenceStatus
{
    Pending,
    Confirmed,
    Declined,
}
