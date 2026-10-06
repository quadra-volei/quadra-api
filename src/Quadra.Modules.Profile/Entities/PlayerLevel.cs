namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// Player skill level. A player self-declares <see cref="Beginner"/>, <see cref="Intermediate"/>
/// or <see cref="Advanced"/> at onboarding; afterwards the level is recalculated from recorded
/// matches and never drops below the declared one. <see cref="Elite"/> cannot be declared and is
/// not computed yet (it depends on the global ranking, see <c>docs/PRODUCT.md</c>).
/// Declaration order is the ranking order (higher value = higher level).
/// </summary>
public enum PlayerLevel
{
    Beginner = 0,
    Intermediate = 1,
    Advanced = 2,
    Elite = 3,
}
