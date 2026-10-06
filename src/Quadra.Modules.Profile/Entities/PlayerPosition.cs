namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// The single court position a player declares (frontend S4/S10). Persisted and exposed by its
/// three-letter code, the same code the app shows.
/// </summary>
public enum PlayerPosition
{
    /// <summary>Levantador (setter).</summary>
    LEV,

    /// <summary>Ponteiro (outside hitter).</summary>
    PON,

    /// <summary>Oposto (opposite).</summary>
    OPO,

    /// <summary>Central (middle blocker).</summary>
    CEN,

    /// <summary>Líbero.</summary>
    LIB,

    /// <summary>Coringa — plays any position.</summary>
    COR,
}
