namespace Quadra.Modules.Profile.Abstractions;

/// <summary>
/// Profile-owned write interface that bootstraps an empty profile + stats pair for a user.
/// Invoked by the Background Worker when it consumes <c>UserRegistered</c>. Idempotent.
/// </summary>
public interface IPlayerProfileProvisioner
{
    /// <summary>
    /// Ensures a <c>player_profiles</c> + <c>player_stats</c> pair exists for <paramref name="userId"/>.
    /// A no-op when the profile already exists (covers at-least-once redelivery).
    /// </summary>
    Task EnsureProfileAsync(Guid userId, CancellationToken cancellationToken);
}
