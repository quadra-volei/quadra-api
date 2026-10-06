using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// EF Core DbContext owned by the Matches module. Only the <c>matches</c> table is mapped here.
/// Module-boundary rules forbid cross-module joins.
/// </summary>
public sealed class MatchesDbContext : DbContext
{
    public MatchesDbContext(DbContextOptions<MatchesDbContext> options)
        : base(options)
    {
    }

    public DbSet<Match> Matches => Set<Match>();
    public DbSet<MatchPresence> Presences => Set<MatchPresence>();
    public DbSet<WaitingListEntry> WaitingList => Set<WaitingListEntry>();

    public DbSet<MatchGuest> Guests => Set<MatchGuest>();
    public DbSet<MatchSummary> MatchSummaries => Set<MatchSummary>();
    public DbSet<MatchSummarySet> MatchSummarySets => Set<MatchSummarySet>();
    public DbSet<MatchSummaryPlayer> MatchSummaryPlayers => Set<MatchSummaryPlayer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new MatchConfiguration());
        modelBuilder.ApplyConfiguration(new MatchPresenceConfiguration());
        modelBuilder.ApplyConfiguration(new WaitingListConfiguration());
        modelBuilder.ApplyConfiguration(new MatchGuestConfiguration());
        modelBuilder.ApplyConfiguration(new MatchSummaryConfiguration());
        modelBuilder.ApplyConfiguration(new MatchSummarySetConfiguration());
        modelBuilder.ApplyConfiguration(new MatchSummaryPlayerConfiguration());
    }
}
