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
}
