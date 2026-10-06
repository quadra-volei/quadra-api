using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="MatchSummary"/> entity.
/// Maps to the <c>match_summaries</c> table with snake_case column names.
/// The record is immutable — there is deliberately no <c>updated_at</c> column.
/// </summary>
public sealed class MatchSummaryConfiguration : IEntityTypeConfiguration<MatchSummary>
{
    public void Configure(EntityTypeBuilder<MatchSummary> builder)
    {
        builder.ToTable("match_summaries");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(s => s.Format)
            .HasColumnName("format")
            .HasMaxLength(16)
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
            .IsRequired();

        builder.Property(s => s.TeamBSetsWon)
            .HasColumnName("team_b_sets_won")
            .HasColumnType("smallint")
            .IsRequired();

        builder.Property(s => s.WinnerTeamId)
            .HasColumnName("winner_team_id");

        builder.Property(s => s.StartedAt)
            .HasColumnName("started_at")
            .IsRequired();

        builder.Property(s => s.EndedAt)
            .HasColumnName("ended_at")
            .IsRequired();

        builder.Property(s => s.DurationSeconds)
            .HasColumnName("duration_seconds")
            .HasColumnType("integer")
            .IsRequired();

        builder.Property(s => s.MvpPlayerId)
            .HasColumnName("mvp_player_id");

        builder.Property(s => s.MvpVoteCount)
            .HasColumnName("mvp_vote_count")
            .HasColumnType("smallint");

        builder.Property(s => s.MvpTotalVotes)
            .HasColumnName("mvp_total_votes")
            .HasColumnType("smallint")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.GeneratedBy)
            .HasColumnName("generated_by")
            .IsRequired();

        builder.Property(s => s.GeneratedAt)
            .HasColumnName("generated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: at most one summary per match (immutability guard).
        builder.HasIndex(s => s.MatchId)
            .IsUnique()
            .HasDatabaseName("uq_match_summaries_match_id");

        // Sets and Players are in-memory collections not mapped as EF navigations.
        // The repository loads them in separate queries and attaches them.
        // This avoids generating DDL FK constraints (module boundary rule applies within module too).
        builder.Ignore(s => s.Sets);
        builder.Ignore(s => s.Players);
    }
}
