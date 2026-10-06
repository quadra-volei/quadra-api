using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IMatchSummaryRepository"/>.
/// </summary>
public sealed class MatchSummaryRepository : IMatchSummaryRepository
{
    private readonly MatchesDbContext _context;

    public MatchSummaryRepository(MatchesDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task AddAsync(MatchSummary summary, CancellationToken cancellationToken)
    {
        await _context.MatchSummaries.AddAsync(summary, cancellationToken);

        if (summary.Sets.Count > 0)
        {
            await _context.MatchSummarySets.AddRangeAsync(summary.Sets, cancellationToken);
        }

        if (summary.Players.Count > 0)
        {
            await _context.MatchSummaryPlayers.AddRangeAsync(summary.Players, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<MatchSummary?> FindByMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var summary = await _context.MatchSummaries
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.MatchId == matchId, cancellationToken);

        if (summary is null)
        {
            return null;
        }

        var sets = await _context.MatchSummarySets
            .AsNoTracking()
            .Where(s => s.MatchSummaryId == summary.Id)
            .OrderBy(s => s.SetNumber)
            .ToListAsync(cancellationToken);

        var players = await _context.MatchSummaryPlayers
            .AsNoTracking()
            .Where(p => p.MatchSummaryId == summary.Id)
            .ToListAsync(cancellationToken);

        summary.AttachSets(sets);
        summary.AttachPlayers(players);

        return summary;
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsForMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        return await _context.MatchSummaries
            .AsNoTracking()
            .AnyAsync(s => s.MatchId == matchId, cancellationToken);
    }
}
