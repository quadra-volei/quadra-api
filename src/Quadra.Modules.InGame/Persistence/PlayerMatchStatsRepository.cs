using Microsoft.EntityFrameworkCore;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

public sealed class PlayerMatchStatsRepository : IPlayerMatchStatsRepository
{
    private readonly InGameDbContext _context;

    public PlayerMatchStatsRepository(InGameDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<PlayerMatchStats>> ListByMatchAsync(Guid matchId, CancellationToken cancellationToken) =>
        await _context.PlayerMatchStats
            .Where(s => s.MatchId == matchId)
            .OrderBy(s => s.PlayerId)
            .ToListAsync(cancellationToken);

    public async Task ReplaceAsync(Guid matchId, IReadOnlyList<PlayerMatchStats> rows, CancellationToken cancellationToken)
    {
        // Rows come from ListByMatchAsync (tracked) or are new; the ones not kept are removed.
        var kept = rows.Select(r => r.Id).ToHashSet();
        var stale = await _context.PlayerMatchStats
            .Where(s => s.MatchId == matchId && !kept.Contains(s.Id))
            .ToListAsync(cancellationToken);
        _context.PlayerMatchStats.RemoveRange(stale);

        foreach (var row in rows.Where(r => _context.Entry(r).State == EntityState.Detached))
        {
            _context.PlayerMatchStats.Add(row);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
