using Microsoft.EntityFrameworkCore;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IScoreboardRepository"/>.
/// </summary>
public sealed class ScoreboardRepository : IScoreboardRepository
{
    private readonly InGameDbContext _context;

    public ScoreboardRepository(InGameDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task AddAsync(Scoreboard scoreboard, CancellationToken cancellationToken)
    {
        await _context.Scoreboards.AddAsync(scoreboard, cancellationToken);

        if (scoreboard.Sets.Count > 0)
        {
            await _context.ScoreboardSets.AddRangeAsync(scoreboard.Sets, cancellationToken);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Scoreboard?> FindByMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var scoreboard = await _context.Scoreboards
            .FirstOrDefaultAsync(s => s.MatchId == matchId, cancellationToken);

        if (scoreboard is null)
        {
            return null;
        }

        var sets = await _context.ScoreboardSets
            .Where(s => s.ScoreboardId == scoreboard.Id)
            .OrderBy(s => s.SetNumber)
            .ToListAsync(cancellationToken);

        scoreboard.AttachSets(sets);

        return scoreboard;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(Scoreboard scoreboard, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // Existing scoreboard and sets are change-tracked from FindByMatchAsync; newly opened sets
        // are detached and must be inserted explicitly.
        foreach (var set in scoreboard.Sets)
        {
            if (_context.Entry(set).State == EntityState.Detached)
            {
                await _context.ScoreboardSets.AddAsync(set, cancellationToken);
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
