using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>EF Core implementation of <see cref="IMatchGuestRepository"/>.</summary>
public sealed class MatchGuestRepository : IMatchGuestRepository
{
    private readonly MatchesDbContext _context;

    public MatchGuestRepository(MatchesDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(MatchGuest guest, CancellationToken cancellationToken)
    {
        await _context.Guests.AddAsync(guest, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MatchGuest?> FindAsync(Guid matchId, Guid guestId, CancellationToken cancellationToken)
    {
        return await _context.Guests
            .FirstOrDefaultAsync(g => g.MatchId == matchId && g.Id == guestId, cancellationToken);
    }

    public async Task RemoveAsync(MatchGuest guest, CancellationToken cancellationToken)
    {
        _context.Guests.Remove(guest);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MatchGuest>> ListByMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        return await _context.Guests
            .AsNoTracking()
            .Where(g => g.MatchId == matchId)
            .OrderBy(g => g.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountByMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        return await _context.Guests.CountAsync(g => g.MatchId == matchId, cancellationToken);
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountByMatchesAsync(
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken)
    {
        var ids = matchIds.ToArray();
        return await _context.Guests
            .AsNoTracking()
            .Where(g => ids.Contains(g.MatchId))
            .GroupBy(g => g.MatchId)
            .Select(g => new { MatchId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.MatchId, g => g.Count, cancellationToken);
    }
}
