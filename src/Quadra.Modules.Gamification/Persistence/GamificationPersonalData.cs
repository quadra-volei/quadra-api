using Microsoft.EntityFrameworkCore;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Gamification.Persistence;

/// <summary>
/// Gamification part of account export and deletion (SEC-14): the person's points and group
/// ranking lines. Deletion takes them out of every ranking.
/// </summary>
public sealed class GamificationPersonalData : IPersonalDataStore
{
    private readonly GamificationDbContext _db;

    public GamificationPersonalData(GamificationDbContext db)
    {
        _db = db;
    }

    public string Section => "ranking";

    public async Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken) => new
    {
        Points = await _db.PointTransactions.AsNoTracking().Where(p => p.UserId == userId).ToListAsync(cancellationToken),
        GroupRankings = await _db.GroupRankings.AsNoTracking().Where(r => r.UserId == userId).ToListAsync(cancellationToken),
    };

    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _db.PointTransactions.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.GroupRankings.Where(r => r.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}
