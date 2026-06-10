using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IWaitingListRepository"/>.
/// </summary>
public sealed class WaitingListRepository : IWaitingListRepository
{
    private readonly MatchesDbContext _context;

    public WaitingListRepository(MatchesDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(WaitingListEntry entry, CancellationToken cancellationToken)
    {
        await _context.WaitingList.AddAsync(entry, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<WaitingListEntry?> FindByMatchAndPlayerAsync(
        Guid matchId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        return await _context.WaitingList
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.MatchId == matchId && w.PlayerId == playerId, cancellationToken);
    }

    public async Task<IReadOnlyList<WaitingListEntry>> ListByMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        return await _context.WaitingList
            .AsNoTracking()
            .Where(w => w.MatchId == matchId)
            .OrderBy(w => w.Position)
            .ToListAsync(cancellationToken);
    }

    public async Task RemoveAsync(WaitingListEntry entry, CancellationToken cancellationToken)
    {
        _context.WaitingList.Remove(entry);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> GetNextPositionAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var max = await _context.WaitingList
            .Where(w => w.MatchId == matchId)
            .MaxAsync(w => (int?)w.Position, cancellationToken);

        return (max ?? 0) + 1;
    }
}
