using Quadra.Modules.Gamification.Entities;

namespace Quadra.Modules.Gamification.Persistence;

/// <summary>Persistence contract for the materialized <see cref="GroupRanking"/> standings.</summary>
public interface IGroupRankingRepository
{
    /// <summary>
    /// Inserts or overwrites the standing row identified by <c>(group_id, user_id)</c> with the
    /// recomputed values (recompute-not-increment).
    /// </summary>
    Task UpsertAsync(
        Guid groupId,
        Guid userId,
        int totalPoints,
        int matchesCounted,
        Guid? lastMatchId,
        DateTimeOffset? lastMatchDateTime,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns one page of standings for a group, ordered <c>total_points DESC, user_id ASC</c>.
    /// </summary>
    Task<IReadOnlyList<GroupRanking>> GetPageAsync(
        Guid groupId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>Total number of standing rows for a group.</summary>
    Task<long> CountByGroupAsync(Guid groupId, CancellationToken cancellationToken);

    /// <summary>The caller's own standing, or <c>null</c> when they have no row in the group.</summary>
    Task<GroupRanking?> FindEntryAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// The caller's 1-based absolute rank in the <c>total_points DESC, user_id ASC</c> ordering,
    /// or <c>null</c> when they have no row in the group.
    /// </summary>
    /// <summary>The group where the user scored most recently; null when they never scored.</summary>
    Task<Guid?> FindLatestGroupIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<int?> GetRankAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);
}
