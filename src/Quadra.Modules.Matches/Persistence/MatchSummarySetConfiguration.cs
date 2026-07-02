using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="MatchSummarySet"/> entity.
/// Maps to the <c>match_summary_sets</c> table with snake_case column names.
/// </summary>
public sealed class MatchSummarySetConfiguration : IEntityTypeConfiguration<MatchSummarySet>
{
    public void Configure(EntityTypeBuilder<MatchSummarySet> builder)
    {
        builder.ToTable("match_summary_sets");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.MatchSummaryId)
            .HasColumnName("match_summary_id")
            .IsRequired();

        builder.Property(s => s.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(s => s.SetNumber)
            .HasColumnName("set_number")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(s => s.TeamAPoints)
            .HasColumnName("team_a_points")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(s => s.TeamBPoints)
            .HasColumnName("team_b_points")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(s => s.WinnerTeamId)
            .HasColumnName("winner_team_id");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: each set number appears once per summary.
        builder.HasIndex(s => new { s.MatchSummaryId, s.SetNumber })
            .IsUnique()
            .HasDatabaseName("uq_match_summary_sets_summary_set_number");

        // Indexes for per-summary and per-match lookups.
        builder.HasIndex(s => s.MatchSummaryId)
            .HasDatabaseName("ix_match_summary_sets_match_summary_id");

        builder.HasIndex(s => s.MatchId)
            .HasDatabaseName("ix_match_summary_sets_match_id");
    }
}
