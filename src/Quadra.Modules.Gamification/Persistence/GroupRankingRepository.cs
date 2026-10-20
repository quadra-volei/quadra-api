using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Gamification.Entities;

namespace Quadra.Modules.Gamification.Persistence;

/// <summary>EF Core implementation of <see cref="IGroupRankingRepository"/>.</summary>
public sealed class GroupRankingRepository : IGroupRankingRepository
{
    private readonly GamificationDbContext _context;

    public GroupRankingRepository(GamificationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(
        Guid groupId,
        Guid userId,
        int totalPoints,
        int matchesCounted,
        Guid? lastMatchId,
        DateTimeOffset? lastMatchDateTime,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await _context.GroupRankings
            .FirstOrDefaultAsync(r => r.GroupId == groupId && r.UserId == userId, cancellationToken);

        if (existing is null)
        {
            var ranking = GroupRanking.Empty(groupId, userId, now);
            ranking.Set(totalPoints, matchesCounted, lastMatchId, lastMatchDateTime, now);
            await _context.GroupRankings.AddAsync(ranking, cancellationToken);
        }
        else
        {
            existing.Set(totalPoints, matchesCounted, lastMatchId, lastMatchDateTime, now);
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<GroupRanking>> GetPageAsync(
        Guid groupId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        return await _context.GroupRankings
            .AsNoTracking()
            .Where(r => r.GroupId == groupId)
            .OrderByDescending(r => r.TotalPoints)
            .ThenBy(r => r.UserId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<long> CountByGroupAsync(Guid groupId, CancellationToken cancellationToken)
    {
        return await _context.GroupRankings
            .AsNoTracking()
            .LongCountAsync(r => r.GroupId == groupId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<GroupRanking?> FindEntryAsync(
        Guid groupId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _context.GroupRankings
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.GroupId == groupId && r.UserId == userId, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<Guid?> FindLatestGroupIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.GroupRankings
            .AsNoTracking()
            .Where(r => r.UserId == userId)
            .OrderByDescending(r => r.LastMatchDateTime)
            .Select(r => (Guid?)r.GroupId)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int?> GetRankAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        var caller = await _context.GroupRankings
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.GroupId == groupId && r.UserId == userId, cancellationToken);

        if (caller is null)
        {
            return null;
        }

        // Number of players strictly ahead in the (total_points DESC, user_id ASC) ordering, +1.
        var ahead = await _context.GroupRankings
            .AsNoTracking()
            .Where(r => r.GroupId == groupId)
            .CountAsync(
                r => r.TotalPoints > caller.TotalPoints
                     || (r.TotalPoints == caller.TotalPoints && r.UserId < caller.UserId),
                cancellationToken);

        return ahead + 1;
    }
}
