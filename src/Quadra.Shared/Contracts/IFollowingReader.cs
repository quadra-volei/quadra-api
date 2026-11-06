namespace Quadra.Shared.Contracts;

/// <summary>
/// A player's network, implemented by the Profile module. The network ranking (Gamification)
/// asks through it instead of reading the follows table.
/// </summary>
public interface IFollowingReader
{
    /// <summary>The ids of the players <paramref name="userId"/> follows or is followed by (empty when none).</summary>
    Task<IReadOnlyList<Guid>> GetNetworkIdsAsync(Guid userId, CancellationToken cancellationToken);
}
