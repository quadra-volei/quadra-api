using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="MatchSummaryPlayer"/> entity.
/// Maps to the <c>match_summary_players</c> table with snake_case column names.
/// </summary>
public sealed class MatchSummaryPlayerConfiguration : IEntityTypeConfiguration<MatchSummaryPlayer>
{
    public void Configure(EntityTypeBuilder<MatchSummaryPlayer> builder)
    {
        builder.ToTable("match_summary_players");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.MatchSummaryId)
            .HasColumnName("match_summary_id")
            .IsRequired();

        builder.Property(p => p.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(p => p.TeamId)
            .HasColumnName("team_id")
            .IsRequired();

        builder.Property(p => p.TeamName)
            .HasColumnName("team_name")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: a player appears once in a summary.
        builder.HasIndex(p => new { p.MatchSummaryId, p.PlayerId })
            .IsUnique()
            .HasDatabaseName("uq_match_summary_players_summary_player");

        // Indexes for per-summary and per-match lookups.
        builder.HasIndex(p => p.MatchSummaryId)
            .HasDatabaseName("ix_match_summary_players_match_summary_id");

        builder.HasIndex(p => p.MatchId)
            .HasDatabaseName("ix_match_summary_players_match_id");
    }
}
