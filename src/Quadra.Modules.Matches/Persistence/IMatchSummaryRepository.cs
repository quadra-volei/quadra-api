using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Persistence contract for the immutable <see cref="MatchSummary"/> aggregate
/// (summary + its sets + its players). No update or delete operations exist — the record is immutable.
/// </summary>
public interface IMatchSummaryRepository
{
    /// <summary>
    /// Inserts a new <paramref name="summary"/> together with its sets and players in a single save.
    /// </summary>
    Task AddAsync(MatchSummary summary, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the summary for <paramref name="matchId"/> with its sets and players attached,
    /// or <c>null</c> if no summary exists for the match.
    /// </summary>
    Task<MatchSummary?> FindByMatchAsync(Guid matchId, CancellationToken cancellationToken);

    /// <summary>
    /// Returns <c>true</c> when a summary already exists for <paramref name="matchId"/>.
    /// </summary>
    Task<bool> ExistsForMatchAsync(Guid matchId, CancellationToken cancellationToken);
}
