using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="Scoreboard"/> entity.
/// Maps to the <c>scoreboards</c> table with snake_case column names.
/// </summary>
public sealed class ScoreboardConfiguration : IEntityTypeConfiguration<Scoreboard>
{
    public void Configure(EntityTypeBuilder<Scoreboard> builder)
    {
        builder.ToTable("scoreboards");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(s => s.Format)
            .HasColumnName("format")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(s => s.State)
            .HasColumnName("state")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(ScoreboardState.NotStarted)
            .IsRequired();

        builder.Property(s => s.TeamAId)
            .HasColumnName("team_a_id")
            .IsRequired();

        builder.Property(s => s.TeamBId)
            .HasColumnName("team_b_id")
            .IsRequired();

        builder.Property(s => s.TeamASetsWon)
            .HasColumnName("team_a_sets_won")
            .HasColumnType("smallint")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.TeamBSetsWon)
            .HasColumnName("team_b_sets_won")
            .HasColumnType("smallint")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.CurrentSetNumber)
            .HasColumnName("current_set_number")
            .HasColumnType("smallint")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.WinnerTeamId)
            .HasColumnName("winner_team_id");

        builder.Property(s => s.StartedAt)
            .HasColumnName("started_at");

        builder.Property(s => s.EndedAt)
            .HasColumnName("ended_at");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: at most one scoreboard per match.
        builder.HasIndex(s => s.MatchId)
            .IsUnique()
            .HasDatabaseName("uq_scoreboards_match_id");

        // Sets is an in-memory collection not mapped as an EF navigation.
        // The repository loads sets in a separate query and attaches them via AttachSets.
        // This avoids generating a DDL FK constraint (module boundary rule applies within module too).
        builder.Ignore(s => s.Sets);
    }
}
