using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Persistence contract for the <see cref="Match"/> aggregate.
/// </summary>
public interface IMatchRepository
{
    Task AddAsync(Match match, CancellationToken cancellationToken);
    Task<Match?> FindByIdAsync(Guid id, CancellationToken cancellationToken);
    Task UpdateAsync(Match match, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a filtered, ordered, paginated page of matches together with the total count
    /// of rows that match the filter (before paging).
    /// </summary>
    Task<(IReadOnlyList<Match> Items, int TotalCount)> ListAsync(
        ListMatchesFilter filter,
        CancellationToken cancellationToken);
}

/// <summary>
/// Filter criteria for <see cref="IMatchRepository.ListAsync"/>.
/// </summary>
public sealed record ListMatchesFilter(
    Guid? OrganizerId,
    MatchStatus? Status,
    int Page,
    int PageSize);
