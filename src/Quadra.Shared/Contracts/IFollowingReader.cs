namespace Quadra.Shared.Contracts;

/// <summary>
/// Who a player follows, implemented by the Profile module. The friends ranking (Gamification)
/// asks through it instead of reading the follows table.
/// </summary>
public interface IFollowingReader
{
    /// <summary>The ids of the players <paramref name="userId"/> follows (empty when none).</summary>
    Task<IReadOnlyList<Guid>> GetFollowingIdsAsync(Guid userId, CancellationToken cancellationToken);
}
