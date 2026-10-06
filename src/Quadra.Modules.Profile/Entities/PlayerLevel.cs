namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// The automatically derived skill level of a player. Persisted as its string name.
/// MVP produces only the two tiers below; <c>Advanced</c>/<c>Elite</c> are deferred and will be
/// added to this enum when their tiers are activated (the column is a string, so no migration).
/// </summary>
public enum PlayerLevel
{
    Beginner,
    Intermediate,
}
