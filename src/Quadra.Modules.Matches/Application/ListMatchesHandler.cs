using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Returns a paginated, filtered list of matches.
/// </summary>
public sealed class ListMatchesHandler
{
    private readonly IMatchRepository _repository;

    public ListMatchesHandler(IMatchRepository repository)
    {
        _repository = repository;
    }

    public async Task<(IReadOnlyList<Match> Items, int TotalCount)> HandleAsync(
        ListMatchesQuery query,
        CancellationToken cancellationToken)
    {
        MatchStatus? status = query.Status is not null
            ? Enum.Parse<MatchStatus>(query.Status, ignoreCase: true)
            : null;

        var filter = new ListMatchesFilter(
            OrganizerId: query.OrganizerId,
            Status: status,
            Page: query.Page,
            PageSize: query.PageSize);

        return await _repository.ListAsync(filter, cancellationToken);
    }
}
