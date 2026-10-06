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
        Detach(match);
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
        Detach(match);
    }

    // Matches are always read untracked, so a saved instance must not stay tracked: a later
    // update of a freshly read copy in the same scope would collide with it.
    private void Detach(Match match) => _context.Entry(match).State = EntityState.Detached;

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

    public async Task<IReadOnlyList<Match>> ListWindowDueAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        return await _context.Matches
            .AsNoTracking()
            .Where(m =>
                (m.Status == MatchStatus.Draft && m.WindowOpensAt <= now)
                || (m.Status == MatchStatus.Open && m.WindowClosesAt < now))
            .OrderBy(m => m.WindowOpensAt)
            .Take(200)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Match>> ListUpcomingForPlayerAsync(
        Guid playerId,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        return await _context.Matches
            .AsNoTracking()
            .Where(m => m.DateTime >= since)
            .Where(m => m.Status != MatchStatus.Cancelled && m.Status != MatchStatus.Ended)
            .Where(m =>
                m.OrganizerId == playerId
                || _context.Presences.Any(p =>
                    p.MatchId == m.Id && p.PlayerId == playerId && p.Status != PresenceStatus.Declined)
                || _context.WaitingList.Any(w => w.MatchId == m.Id && w.PlayerId == playerId))
            .OrderBy(m => m.DateTime)
            .Take(50)
            .ToListAsync(cancellationToken);
    }
}
