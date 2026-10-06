using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Gamification.Entities;

namespace Quadra.Modules.Gamification.Persistence;

/// <summary>
/// EF Core DbContext owned by the Gamification module. Owns the <c>point_transactions</c> and
/// <c>group_rankings</c> tables. Module-boundary rules forbid cross-module joins.
/// </summary>
public sealed class GamificationDbContext : DbContext
{
    public GamificationDbContext(DbContextOptions<GamificationDbContext> options)
        : base(options)
    {
    }

    public DbSet<PointTransaction> PointTransactions => Set<PointTransaction>();
    public DbSet<GroupRanking> GroupRankings => Set<GroupRanking>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new PointTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new GroupRankingConfiguration());
    }
}
