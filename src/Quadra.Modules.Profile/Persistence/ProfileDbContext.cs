using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>
/// EF Core DbContext owned by the Profile module.
/// Owns the <c>player_profiles</c>, <c>player_stats</c>, <c>player_match_history</c> and
/// <c>player_cards</c>, <c>feedback</c> and <c>posts</c> tables. Module-boundary rules forbid cross-module joins.
/// </summary>
public sealed class ProfileDbContext : DbContext
{
    public ProfileDbContext(DbContextOptions<ProfileDbContext> options)
        : base(options)
    {
    }

    public DbSet<PlayerProfile> PlayerProfiles => Set<PlayerProfile>();
    public DbSet<PlayerStats> PlayerStats => Set<PlayerStats>();
    public DbSet<PlayerMatchHistoryEntry> PlayerMatchHistory => Set<PlayerMatchHistoryEntry>();
    public DbSet<PlayerCard> PlayerCards => Set<PlayerCard>();
    public DbSet<Quadra.Modules.Profile.Feedback.FeedbackEntry> Feedback => Set<Quadra.Modules.Profile.Feedback.FeedbackEntry>();
    public DbSet<Quadra.Modules.Profile.Posts.Post> Posts => Set<Quadra.Modules.Profile.Posts.Post>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new PlayerProfileConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerStatsConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerMatchHistoryEntryConfiguration());
        modelBuilder.ApplyConfiguration(new PlayerCardConfiguration());
        modelBuilder.ApplyConfiguration(new Quadra.Modules.Profile.Feedback.FeedbackEntryConfiguration());
        modelBuilder.ApplyConfiguration(new Quadra.Modules.Profile.Posts.PostConfiguration());
    }
}
