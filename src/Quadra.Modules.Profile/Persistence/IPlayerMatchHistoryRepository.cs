using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>Aggregated outcome counts for a player, computed from their match-history rows.</summary>
public sealed record OutcomeCounts(int Wins, int Losses, int Draws, int MvpsReceived);

/// <summary>Persistence contract for <see cref="PlayerMatchHistoryEntry"/> rows.</summary>
public interface IPlayerMatchHistoryRepository
{
    /// <summary>
    /// Returns one page of history rows for <paramref name="userId"/>, ordered newest-first
    /// (<c>match_date_time DESC, match_id DESC</c>).
    /// </summary>
    Task<IReadOnlyList<PlayerMatchHistoryEntry>> GetPageAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or (on redelivery) re-writes the history row identified by <c>(user_id, match_id)</c>.
    /// </summary>
    Task UpsertAsync(PlayerMatchHistoryEntry entry, CancellationToken cancellationToken);

    /// <summary>Total number of history rows for <paramref name="userId"/>.</summary>
    Task<long> CountByUserAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Aggregated win/loss/draw and MVP counts derived from the player's history rows.</summary>
    Task<OutcomeCounts> GetOutcomeCountsAsync(Guid userId, CancellationToken cancellationToken);
}
