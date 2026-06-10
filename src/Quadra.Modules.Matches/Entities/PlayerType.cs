namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Indicates whether a player is a Regular (invited by the organizer) or a DropIn (open-slot player).
/// Persisted as a string in the database.
/// </summary>
public enum PlayerType
{
    Regular,
    DropIn,
}
