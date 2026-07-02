using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="MvpVote"/> entity.
/// Maps to the <c>mvp_votes</c> table with snake_case column names.
/// </summary>
public sealed class MvpVoteConfiguration : IEntityTypeConfiguration<MvpVote>
{
    public void Configure(EntityTypeBuilder<MvpVote> builder)
    {
        builder.ToTable("mvp_votes", t =>
            t.HasCheckConstraint("ck_mvp_votes_no_self_vote", "voter_player_id <> voted_player_id"));

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(v => v.VotingId)
            .HasColumnName("voting_id")
            .IsRequired();

        builder.Property(v => v.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(v => v.VoterPlayerId)
            .HasColumnName("voter_player_id")
            .IsRequired();

        builder.Property(v => v.VotedPlayerId)
            .HasColumnName("voted_player_id")
            .IsRequired();

        builder.Property(v => v.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(v => v.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: a participant may hold only one vote per match.
        builder.HasIndex(v => new { v.MatchId, v.VoterPlayerId })
            .IsUnique()
            .HasDatabaseName("uq_mvp_votes_match_voter");

        // Index for per-session lookups.
        builder.HasIndex(v => v.VotingId)
            .HasDatabaseName("ix_mvp_votes_voting_id");

        // Index for tally aggregation.
        builder.HasIndex(v => new { v.MatchId, v.VotedPlayerId })
            .HasDatabaseName("ix_mvp_votes_match_voted");
    }
}
