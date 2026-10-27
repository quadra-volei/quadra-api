using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Gamification.Entities;

namespace Quadra.Modules.Gamification.Persistence;

/// <summary>EF Core implementation of <see cref="IPointTransactionRepository"/>.</summary>
public sealed class PointTransactionRepository : IPointTransactionRepository
{
    private readonly GamificationDbContext _context;

    public PointTransactionRepository(GamificationDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task AddIfAbsentAsync(PointTransaction transaction, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);

        var exists = await _context.PointTransactions
            .AnyAsync(
                t => t.UserId == transaction.UserId
                     && t.MatchId == transaction.MatchId
                     && t.Reason == transaction.Reason,
                cancellationToken);

        if (exists)
        {
            return;
        }

        await _context.PointTransactions.AddAsync(transaction, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> SumByGroupUserAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        return await _context.PointTransactions
            .AsNoTracking()
            .Where(t => t.GroupId == groupId && t.UserId == userId)
            .SumAsync(t => t.Points, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<int> CountAttendedMatchesAsync(Guid groupId, Guid userId, CancellationToken cancellationToken)
    {
        return await _context.PointTransactions
            .AsNoTracking()
            .Where(t => t.GroupId == groupId && t.UserId == userId && t.Reason == PointReason.Attendance)
            .Select(t => t.MatchId)
            .Distinct()
            .CountAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<AttendedMatch?> LatestAttendedMatchAsync(
        Guid groupId,
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await _context.PointTransactions
            .AsNoTracking()
            .Where(t => t.GroupId == groupId && t.UserId == userId && t.Reason == PointReason.Attendance)
            .OrderByDescending(t => t.MatchDateTime)
            .ThenByDescending(t => t.MatchId)
            .Select(t => new AttendedMatch(t.MatchId, t.MatchDateTime))
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<SeasonTotals>> GetTotalsSinceAsync(
        DateTimeOffset since,
        DateTimeOffset until,
        Guid? groupId,
        IReadOnlyCollection<Guid>? userIds,
        CancellationToken cancellationToken)
    {
        var sinceUtc = since.ToUniversalTime(); // Npgsql writes only UTC offsets
        var untilUtc = until.ToUniversalTime();
        var query = _context.PointTransactions.AsNoTracking()
            .Where(t => t.MatchDateTime >= sinceUtc && t.MatchDateTime < untilUtc);
        if (groupId is { } group)
        {
            query = query.Where(t => t.GroupId == group);
        }

        if (userIds is not null)
        {
            query = query.Where(t => userIds.Contains(t.UserId));
        }

        // One row per player, match and reason (AddIfAbsentAsync), so counting Attendance rows counts
        // matches. Games won and votes are the quantities; an old Win row (before #95) is one game.
        return await query
            .GroupBy(t => t.UserId)
            .Select(g => new SeasonTotals(
                g.Key,
                g.Sum(t => t.Points),
                g.Count(t => t.Reason == PointReason.Attendance),
                g.Sum(t => t.Reason == PointReason.GameWin || t.Reason == PointReason.Win ? t.Quantity : 0),
                g.Count(t => t.Reason == PointReason.Mvp),
                g.Sum(t => t.Reason == PointReason.MvpVote ? t.Quantity : 0),
                g.Sum(t => t.Reason == PointReason.Attendance ? t.Points : 0),
                g.Sum(t => t.Reason == PointReason.GameWin || t.Reason == PointReason.Win ? t.Points : 0),
                g.Sum(t => t.Reason == PointReason.MvpVote || t.Reason == PointReason.Mvp ? t.Points : 0)))
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<PointTransaction>> GetMatchAwardsAsync(
        Guid userId,
        Guid matchId,
        CancellationToken cancellationToken) =>
        await _context.PointTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.MatchId == matchId)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<Guid>> GetGroupIdsAsync(Guid userId, CancellationToken cancellationToken) =>
        await _context.PointTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId)
            .GroupBy(t => t.GroupId)
            .OrderByDescending(g => g.Max(t => t.MatchDateTime))
            .Select(g => g.Key)
            .ToListAsync(cancellationToken);

    /// <inheritdoc/>
    public async Task<IReadOnlyList<DatedPoints>> GetPointsAsync(
        Guid userId,
        DateTimeOffset since,
        DateTimeOffset until,
        CancellationToken cancellationToken)
    {
        var sinceUtc = since.ToUniversalTime(); // Npgsql writes only UTC offsets
        var untilUtc = until.ToUniversalTime();
        return await _context.PointTransactions
            .AsNoTracking()
            .Where(t => t.UserId == userId && t.MatchDateTime >= sinceUtc && t.MatchDateTime < untilUtc)
            .Select(t => new DatedPoints(t.MatchDateTime, t.Points))
            .ToListAsync(cancellationToken);
    }
}
