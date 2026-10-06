namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// A player's skill ratings (1–99) as shown on the profile and the player card. Never edited by
/// the player and never stored: they are derived by
/// <see cref="Application.PlayerSkillCalculator"/>.
/// </summary>
/// <param name="Ace">Serve (ACE).</param>
/// <param name="Block">Block (BLK).</param>
/// <param name="Attack">Attack (ATA).</param>
/// <param name="Defense">Defense (DEF).</param>
public sealed record PlayerSkills(int Ace, int Block, int Attack, int Defense)
{
    /// <summary>GERAL: the plain average of the four ratings, rounded.</summary>
    public int Overall => (int)Math.Round((Ace + Block + Attack + Defense) / 4.0, MidpointRounding.AwayFromZero);
}
