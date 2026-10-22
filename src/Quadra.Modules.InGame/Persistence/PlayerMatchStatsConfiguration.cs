using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

public sealed class PlayerMatchStatsConfiguration : IEntityTypeConfiguration<PlayerMatchStats>
{
    public void Configure(EntityTypeBuilder<PlayerMatchStats> builder)
    {
        builder.ToTable("player_match_stats", t =>
        {
            foreach (var column in new[] { "kills", "blocks", "aces", "digs", "assists" })
            {
                t.HasCheckConstraint($"ck_player_match_stats_{column}", $"{column} BETWEEN 0 AND {PlayerMatchStats.MaxCount}");
            }
        });

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(s => s.MatchId).HasColumnName("match_id").IsRequired();
        builder.Property(s => s.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(s => s.Kills).HasColumnName("kills").IsRequired();
        builder.Property(s => s.Blocks).HasColumnName("blocks").IsRequired();
        builder.Property(s => s.Aces).HasColumnName("aces").IsRequired();
        builder.Property(s => s.Digs).HasColumnName("digs").IsRequired();
        builder.Property(s => s.Assists).HasColumnName("assists").IsRequired();
        builder.Property(s => s.RecordedBy).HasColumnName("recorded_by").IsRequired();
        builder.Property(s => s.CreatedAt).HasColumnName("created_at").IsRequired().HasDefaultValueSql("now()");
        builder.Property(s => s.UpdatedAt).HasColumnName("updated_at").IsRequired().HasDefaultValueSql("now()");

        builder.HasIndex(s => new { s.MatchId, s.PlayerId })
            .IsUnique()
            .HasDatabaseName("uq_player_match_stats_match_player");
    }
}
