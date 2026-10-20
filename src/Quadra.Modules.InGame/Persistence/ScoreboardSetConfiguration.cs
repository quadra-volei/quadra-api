using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="ScoreboardSet"/> entity.
/// Maps to the <c>scoreboard_sets</c> table with snake_case column names.
/// </summary>
public sealed class ScoreboardSetConfiguration : IEntityTypeConfiguration<ScoreboardSet>
{
    public void Configure(EntityTypeBuilder<ScoreboardSet> builder)
    {
        builder.ToTable("scoreboard_sets");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.ScoreboardId)
            .HasColumnName("scoreboard_id")
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
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.TeamBPoints)
            .HasColumnName("team_b_points")
            .HasColumnType("smallint")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.Status)
            .HasColumnName("status")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(SetStatus.InProgress)
            .IsRequired();

        builder.Property(s => s.IsDecidingSet)
            .HasColumnName("is_deciding_set")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(s => s.WinnerTeamId)
            .HasColumnName("winner_team_id");

        builder.Property(s => s.TeamAId)
            .HasColumnName("team_a_id");

        builder.Property(s => s.TeamBId)
            .HasColumnName("team_b_id");

        builder.Property(s => s.LastPointTeamId)
            .HasColumnName("last_point_team_id");

        builder.Property(s => s.StartedAt)
            .HasColumnName("started_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(s => s.FinishedAt)
            .HasColumnName("finished_at");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: each set number appears once per scoreboard.
        builder.HasIndex(s => new { s.ScoreboardId, s.SetNumber })
            .IsUnique()
            .HasDatabaseName("uq_scoreboard_sets_scoreboard_set_number");

        // Indexes for per-scoreboard and per-match lookups.
        builder.HasIndex(s => s.ScoreboardId)
            .HasDatabaseName("ix_scoreboard_sets_scoreboard_id");

        builder.HasIndex(s => s.MatchId)
            .HasDatabaseName("ix_scoreboard_sets_match_id");
    }
}
