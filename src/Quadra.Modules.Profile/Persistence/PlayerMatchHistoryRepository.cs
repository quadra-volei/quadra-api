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
            existing.SetActions(entry.Kills, entry.Blocks, entry.Aces, entry.Digs, entry.Assists, entry.ExposureGames ?? 0);
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
        var noResults = rows.Count(r => r.Outcome == MatchOutcome.NoResult);
        var mvps = rows.Count(r => r.WasMvp);

        return new OutcomeCounts(wins, losses, draws, mvps, noResults);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, PlayerActionTotals>> GetActionTotalsAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(userIds);
        var ids = userIds.Distinct().ToArray();
        var cap = Application.PlayerSkillCalculator.MaxGamesPerMatch;

        var rows = await _context.PlayerMatchHistory
            .AsNoTracking()
            .Where(h => ids.Contains(h.UserId) && h.ExposureGames != null)
            .GroupBy(h => h.UserId)
            .Select(g => new
            {
                UserId = g.Key,
                Matches = g.Count(),
                Kills = new { T = g.Sum(h => h.Kills ?? 0), M = g.Count(h => h.Kills != null), G = g.Sum(h => h.Kills != null ? h.ExposureGames!.Value : 0), W = g.Sum(h => h.Kills != null ? Math.Min(h.ExposureGames!.Value, cap) : 0) },
                Blocks = new { T = g.Sum(h => h.Blocks ?? 0), M = g.Count(h => h.Blocks != null), G = g.Sum(h => h.Blocks != null ? h.ExposureGames!.Value : 0), W = g.Sum(h => h.Blocks != null ? Math.Min(h.ExposureGames!.Value, cap) : 0) },
                Aces = new { T = g.Sum(h => h.Aces ?? 0), M = g.Count(h => h.Aces != null), G = g.Sum(h => h.Aces != null ? h.ExposureGames!.Value : 0), W = g.Sum(h => h.Aces != null ? Math.Min(h.ExposureGames!.Value, cap) : 0) },
                Digs = new { T = g.Sum(h => h.Digs ?? 0), M = g.Count(h => h.Digs != null), G = g.Sum(h => h.Digs != null ? h.ExposureGames!.Value : 0), W = g.Sum(h => h.Digs != null ? Math.Min(h.ExposureGames!.Value, cap) : 0) },
                Assists = new { T = g.Sum(h => h.Assists ?? 0), M = g.Count(h => h.Assists != null), G = g.Sum(h => h.Assists != null ? h.ExposureGames!.Value : 0), W = g.Sum(h => h.Assists != null ? Math.Min(h.ExposureGames!.Value, cap) : 0) },
            })
            .ToListAsync(cancellationToken);

        var result = ids.ToDictionary(id => id, _ => PlayerActionTotals.None);
        foreach (var r in rows)
        {
            result[r.UserId] = new PlayerActionTotals(
                r.Matches,
                new ActionTally(r.Kills.T, r.Kills.M, r.Kills.G, r.Kills.W),
                new ActionTally(r.Blocks.T, r.Blocks.M, r.Blocks.G, r.Blocks.W),
                new ActionTally(r.Aces.T, r.Aces.M, r.Aces.G, r.Aces.W),
                new ActionTally(r.Digs.T, r.Digs.M, r.Digs.G, r.Digs.W),
                new ActionTally(r.Assists.T, r.Assists.M, r.Assists.G, r.Assists.W));
        }

        return result;
    }
}
