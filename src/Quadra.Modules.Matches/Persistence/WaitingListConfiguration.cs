using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="WaitingListEntry"/> entity.
/// Maps to the <c>waiting_list</c> table with snake_case column names.
/// </summary>
public sealed class WaitingListConfiguration : IEntityTypeConfiguration<WaitingListEntry>
{
    public void Configure(EntityTypeBuilder<WaitingListEntry> builder)
    {
        builder.ToTable("waiting_list");

        builder.HasKey(w => w.Id);
        builder.Property(w => w.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(w => w.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(w => w.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(w => w.PlayerType)
            .HasColumnName("player_type")
            .HasMaxLength(16)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(w => w.Position)
            .HasColumnName("position")
            .IsRequired();

        builder.Property(w => w.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: a player may appear only once in the waiting list per match.
        builder.HasIndex(w => new { w.MatchId, w.PlayerId })
            .IsUnique()
            .HasDatabaseName("uq_waiting_list_match_player");

        // Unique constraint: positions are unique within a match.
        builder.HasIndex(w => new { w.MatchId, w.Position })
            .IsUnique()
            .HasDatabaseName("uq_waiting_list_match_position");

        // Indexes
        builder.HasIndex(w => w.MatchId)
            .HasDatabaseName("ix_waiting_list_match_id");

        builder.HasIndex(w => new { w.MatchId, w.Position })
            .HasDatabaseName("ix_waiting_list_match_position");
    }
}
