using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="MatchPresence"/> entity.
/// Maps to the <c>match_presences</c> table with snake_case column names.
/// </summary>
public sealed class MatchPresenceConfiguration : IEntityTypeConfiguration<MatchPresence>
{
    public void Configure(EntityTypeBuilder<MatchPresence> builder)
    {
        builder.ToTable("match_presences");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(p => p.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(p => p.PlayerType)
            .HasColumnName("player_type")
            .HasMaxLength(16)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(PresenceStatus.Pending);

        builder.Property(p => p.ConfirmedAt)
            .HasColumnName("confirmed_at");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: a player may have only one presence row per match.
        builder.HasIndex(p => new { p.MatchId, p.PlayerId })
            .IsUnique()
            .HasDatabaseName("uq_match_presences_match_player");

        // Indexes
        builder.HasIndex(p => p.MatchId)
            .HasDatabaseName("ix_match_presences_match_id");

        builder.HasIndex(p => p.PlayerId)
            .HasDatabaseName("ix_match_presences_player_id");

        builder.HasIndex(p => new { p.MatchId, p.Status })
            .HasDatabaseName("ix_match_presences_match_status");

        builder.HasIndex(p => new { p.MatchId, p.PlayerType })
            .HasDatabaseName("ix_match_presences_match_player_type");
    }
}
