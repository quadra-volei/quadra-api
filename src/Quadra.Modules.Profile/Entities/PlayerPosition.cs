namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// The standard five volleyball on-court positions. Persisted as its string name via an EF value
/// converter. This is the full catalogue accepted for a player's primary/secondary position.
/// </summary>
public enum PlayerPosition
{
    Setter,
    OutsideHitter,
    Opposite,
    MiddleBlocker,
    Libero,
}
