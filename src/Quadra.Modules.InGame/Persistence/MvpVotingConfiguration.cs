using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="MvpVoting"/> entity.
/// Maps to the <c>mvp_votings</c> table with snake_case column names.
/// </summary>
public sealed class MvpVotingConfiguration : IEntityTypeConfiguration<MvpVoting>
{
    public void Configure(EntityTypeBuilder<MvpVoting> builder)
    {
        builder.ToTable("mvp_votings");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(v => v.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(v => v.State)
            .HasColumnName("state")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(MvpVotingState.Open)
            .IsRequired();

        builder.Property(v => v.DeadlineAt)
            .HasColumnName("deadline_at")
            .IsRequired();

        builder.Property(v => v.TotalVotes)
            .HasColumnName("total_votes")
            .HasColumnType("smallint")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(v => v.MvpPlayerId)
            .HasColumnName("mvp_player_id");

        builder.Property(v => v.MvpVoteCount)
            .HasColumnName("mvp_vote_count")
            .HasColumnType("smallint");

        builder.Property(v => v.OpenedAt)
            .HasColumnName("opened_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(v => v.ClosedAt)
            .HasColumnName("closed_at");

        builder.Property(v => v.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(v => v.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: at most one voting session per match.
        builder.HasIndex(v => v.MatchId)
            .IsUnique()
            .HasDatabaseName("uq_mvp_votings_match_id");

        // Supports the Background Worker's scheduled query of open votings past their deadline.
        builder.HasIndex(v => new { v.State, v.DeadlineAt })
            .HasDatabaseName("ix_mvp_votings_state_deadline_at");

        // Votes is an in-memory collection not mapped as an EF navigation.
        // The repository loads votes in a separate query and attaches them via AttachVotes.
        // This avoids generating a DDL FK constraint (module boundary rule applies within module too).
        builder.Ignore(v => v.Votes);
    }
}
