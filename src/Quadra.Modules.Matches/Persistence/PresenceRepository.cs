using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IPresenceRepository"/>.
/// </summary>
public sealed class PresenceRepository : IPresenceRepository
{
    private readonly MatchesDbContext _context;

    public PresenceRepository(MatchesDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(MatchPresence presence, CancellationToken cancellationToken)
    {
        await _context.Presences.AddAsync(presence, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<MatchPresence?> FindByMatchAndPlayerAsync(
        Guid matchId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        return await _context.Presences
            .FirstOrDefaultAsync(p => p.MatchId == matchId && p.PlayerId == playerId, cancellationToken);
    }

    public async Task UpdateAsync(MatchPresence presence, CancellationToken cancellationToken)
    {
        _context.Presences.Update(presence);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(MatchPresence presence, CancellationToken cancellationToken)
    {
        _context.Presences.Remove(presence);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MatchPresence>> ListByMatchAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        return await _context.Presences
            .AsNoTracking()
            .Where(p => p.MatchId == matchId)
            .OrderBy(p => p.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountConfirmedAsync(
        Guid matchId,
        PlayerType playerType,
        CancellationToken cancellationToken)
    {
        return await _context.Presences
            .CountAsync(
                p => p.MatchId == matchId
                     && p.PlayerType == playerType
                     && p.Status == PresenceStatus.Confirmed,
                cancellationToken);
    }
}
