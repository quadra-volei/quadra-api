using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>EF Core implementation of <see cref="IPlayerMatchHistoryRepository"/>.</summary>
public sealed class PlayerMatchHistoryRepository : IPlayerMatchHistoryRepository
{
    private readonly ProfileDbContext _context;

    public PlayerMatchHistoryRepository(ProfileDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PlayerMatchHistoryEntry>> GetPageAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return await _context.PlayerMatchHistory
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .OrderByDescending(h => h.MatchDateTime)
            .ThenByDescending(h => h.MatchId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(PlayerMatchHistoryEntry entry, CancellationToken cancellationToken)
    {
        var existing = await _context.PlayerMatchHistory
            .FirstOrDefaultAsync(
                h => h.UserId == entry.UserId && h.MatchId == entry.MatchId,
                cancellationToken);

        if (existing is null)
        {
            await _context.PlayerMatchHistory.AddAsync(entry, cancellationToken);
        }
        else
        {
            existing.UpdateSnapshot(
                entry.MatchName,
                entry.MatchDateTime,
                entry.TeamId,
                entry.Outcome,
                entry.WasMvp,
                entry.DurationSeconds,
                entry.RecordedAt);
            existing.SetScore(entry.Format, entry.SetsWon, entry.SetsLost);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> CountByUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.PlayerMatchHistory
            .AsNoTracking()
            .LongCountAsync(h => h.UserId == userId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<OutcomeCounts> GetOutcomeCountsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await _context.PlayerMatchHistory
            .AsNoTracking()
            .Where(h => h.UserId == userId)
            .Select(h => new { h.Outcome, h.WasMvp })
            .ToListAsync(cancellationToken);

        var wins = rows.Count(r => r.Outcome == MatchOutcome.Win);
        var losses = rows.Count(r => r.Outcome == MatchOutcome.Loss);
        var draws = rows.Count(r => r.Outcome == MatchOutcome.Draw);
        var mvps = rows.Count(r => r.WasMvp);

        return new OutcomeCounts(wins, losses, draws, mvps);
    }
}
