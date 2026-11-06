namespace Quadra.Modules.Profile.Entities;

/// <summary>The look of the player's card. Cosmetic only; persisted by name.</summary>
public enum CardFinish
{
    /// <summary>Prismatic frame, blue background, multicolor glints. The default.</summary>
    Prisma,

    /// <summary>Light surface, green tones, rounded corners.</summary>
    Ion,

    /// <summary>Dark background, gold frame.</summary>
    Eclipse,

    /// <summary>The whole card of the first season pass (T1): only who completed it may equip it (DECISIONS #98).</summary>
    Pioneira,
}
