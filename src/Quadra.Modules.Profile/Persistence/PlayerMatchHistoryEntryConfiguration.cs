using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="PlayerMatchHistoryEntry"/> entity.
/// Maps to the <c>player_match_history</c> table with snake_case column names.
/// </summary>
public sealed class PlayerMatchHistoryEntryConfiguration : IEntityTypeConfiguration<PlayerMatchHistoryEntry>
{
    public void Configure(EntityTypeBuilder<PlayerMatchHistoryEntry> builder)
    {
        builder.ToTable("player_match_history");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(h => h.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(h => h.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(h => h.MatchName)
            .HasColumnName("match_name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(h => h.MatchDateTime)
            .HasColumnName("match_date_time")
            .IsRequired();

        builder.Property(h => h.TeamId)
            .HasColumnName("team_id");

        builder.Property(h => h.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(h => h.WasMvp)
            .HasColumnName("was_mvp")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(h => h.DurationSeconds)
            .HasColumnName("duration_seconds");

        builder.Property(h => h.Format)
            .HasColumnName("format")
            .HasMaxLength(8);

        builder.Property(h => h.SetsWon)
            .HasColumnName("sets_won");

        builder.Property(h => h.SetsLost)
            .HasColumnName("sets_lost");

        builder.Property(h => h.RecordedAt)
            .HasColumnName("recorded_at")
            .IsRequired();

        builder.Property(h => h.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // A match appears once per player — idempotency guard for at-least-once SQS delivery.
        builder.HasIndex(h => new { h.UserId, h.MatchId })
            .IsUnique()
            .HasDatabaseName("uq_player_match_history_user_match");

        // Pagination ordering: newest match first per player.
        builder.HasIndex(h => new { h.UserId, h.MatchDateTime })
            .HasDatabaseName("ix_player_match_history_user_date");

        builder.HasIndex(h => h.MatchId)
            .HasDatabaseName("ix_player_match_history_match_id");
    }
}
