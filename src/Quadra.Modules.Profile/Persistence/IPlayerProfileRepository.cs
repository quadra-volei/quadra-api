using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>A loaded profile together with its stats row.</summary>
public sealed record ProfileWithStats(PlayerProfile Profile, PlayerStats Stats);

/// <summary>Persistence contract for <see cref="PlayerProfile"/> and its <see cref="PlayerStats"/>.</summary>
public interface IPlayerProfileRepository
{
    /// <summary>
    /// Loads the profile and stats for <paramref name="userId"/>, or <c>null</c> when no profile exists.
    /// </summary>
    Task<ProfileWithStats?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Inserts a new profile + stats pair in a single transaction.</summary>
    Task AddAsync(PlayerProfile profile, PlayerStats stats, CancellationToken cancellationToken);

    /// <summary>Persists changes to an already-tracked or detached <paramref name="profile"/>.</summary>
    Task UpdateAsync(PlayerProfile profile, CancellationToken cancellationToken);

    /// <summary>Returns <c>true</c> when a profile already exists for <paramref name="userId"/>.</summary>
    Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken);
}
