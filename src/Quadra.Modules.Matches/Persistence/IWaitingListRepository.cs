using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Persistence contract for the <see cref="WaitingListEntry"/> entity.
/// </summary>
public interface IWaitingListRepository
{
    Task AddAsync(WaitingListEntry entry, CancellationToken cancellationToken);

    Task<WaitingListEntry?> FindByMatchAndPlayerAsync(
        Guid matchId,
        Guid playerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Returns all waiting list entries for the given match, ordered by <c>position ASC</c>.
    /// </summary>
    Task<IReadOnlyList<WaitingListEntry>> ListByMatchAsync(Guid matchId, CancellationToken cancellationToken);

    Task RemoveAsync(WaitingListEntry entry, CancellationToken cancellationToken);

    /// <summary>
    /// Returns <c>MAX(position) + 1</c> for the match, or <c>1</c> if the waiting list is empty.
    /// </summary>
    Task<int> GetNextPositionAsync(Guid matchId, CancellationToken cancellationToken);
}
