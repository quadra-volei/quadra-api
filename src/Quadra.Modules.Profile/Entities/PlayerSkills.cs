namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// A player's skill ratings (1–99) and GERAL as shown on the profile, the card and the ranking.
/// Never edited by the player and never stored: they are derived by
/// <see cref="Application.PlayerSkillCalculator"/> from the declared level and position and the
/// recorded actions.
/// </summary>
/// <param name="Ace">Serve (ACE).</param>
/// <param name="Block">Block (BLK).</param>
/// <param name="Attack">Attack (ATK).</param>
/// <param name="Defense">Defense (DEF).</param>
/// <param name="Overall">GERAL: the baseline average moved by each rating's change, weighted by position.</param>
public sealed record PlayerSkills(int Ace, int Block, int Attack, int Defense, int Overall);
