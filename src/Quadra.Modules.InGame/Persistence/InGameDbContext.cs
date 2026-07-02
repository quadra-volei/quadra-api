using Microsoft.EntityFrameworkCore;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// EF Core DbContext owned by the InGame module.
/// Owns the <c>teams</c>, <c>team_members</c>, <c>scoreboards</c> and <c>scoreboard_sets</c> tables.
/// Module-boundary rules forbid cross-module joins.
/// </summary>
public sealed class InGameDbContext : DbContext
{
    public InGameDbContext(DbContextOptions<InGameDbContext> options)
        : base(options)
    {
    }

    public DbSet<Team> Teams => Set<Team>();
    public DbSet<TeamMember> TeamMembers => Set<TeamMember>();
    public DbSet<Scoreboard> Scoreboards => Set<Scoreboard>();
    public DbSet<ScoreboardSet> ScoreboardSets => Set<ScoreboardSet>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new TeamConfiguration());
        modelBuilder.ApplyConfiguration(new TeamMemberConfiguration());
        modelBuilder.ApplyConfiguration(new ScoreboardConfiguration());
        modelBuilder.ApplyConfiguration(new ScoreboardSetConfiguration());
    }
}
