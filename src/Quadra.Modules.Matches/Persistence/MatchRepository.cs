using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IMatchRepository"/>.
/// </summary>
public sealed class MatchRepository : IMatchRepository
{
    private readonly MatchesDbContext _context;

    public MatchRepository(MatchesDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(Match match, CancellationToken cancellationToken)
    {
        await _context.Matches.AddAsync(match, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<Match?> FindByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        return await _context.Matches
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
    }

    public async Task UpdateAsync(Match match, CancellationToken cancellationToken)
    {
        _context.Matches.Update(match);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<(IReadOnlyList<Match> Items, int TotalCount)> ListAsync(
        ListMatchesFilter filter,
        CancellationToken cancellationToken)
    {
        var query = _context.Matches.AsNoTracking();

        if (filter.OrganizerId.HasValue)
        {
            query = query.Where(m => m.OrganizerId == filter.OrganizerId.Value);
        }

        if (filter.Status.HasValue)
        {
            query = query.Where(m => m.Status == filter.Status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(m => m.DateTime)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .ToListAsync(cancellationToken);

        return (items, totalCount);
    }
}
