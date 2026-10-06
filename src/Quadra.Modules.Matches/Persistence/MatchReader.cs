using Microsoft.EntityFrameworkCore;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Read-only EF query adapter that implements <see cref="IMatchReader"/> over
/// the <c>matches</c> table. Returns only the minimal <see cref="MatchSummary"/>
/// projection needed by cross-module consumers.
/// </summary>
public sealed class MatchReader : IMatchReader
{
    private readonly MatchesDbContext _context;

    public MatchReader(MatchesDbContext context)
    {
        _context = context;
    }

    public async Task<MatchSummary?> FindMatchSummaryAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        return await _context.Matches
            .AsNoTracking()
            .Where(m => m.Id == matchId)
            .Select(m => new MatchSummary(m.Id, m.OrganizerId, m.Status.ToString()))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
