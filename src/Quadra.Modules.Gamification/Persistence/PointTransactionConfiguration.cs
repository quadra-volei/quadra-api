using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Gamification.Entities;

namespace Quadra.Modules.Gamification.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="PointTransaction"/> entity.
/// Maps to the <c>point_transactions</c> table with snake_case column names.
/// </summary>
public sealed class PointTransactionConfiguration : IEntityTypeConfiguration<PointTransaction>
{
    public void Configure(EntityTypeBuilder<PointTransaction> builder)
    {
        builder.ToTable("point_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(t => t.GroupId)
            .HasColumnName("group_id")
            .IsRequired();

        builder.Property(t => t.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(t => t.Reason)
            .HasColumnName("reason")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(t => t.Points)
            .HasColumnName("points")
            .IsRequired();

        builder.Property(t => t.MatchDateTime)
            .HasColumnName("match_date_time")
            .IsRequired();

        builder.Property(t => t.AwardedAt)
            .HasColumnName("awarded_at")
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Each reason is awarded at most once per player per match — the idempotency key.
        builder.HasIndex(t => new { t.UserId, t.MatchId, t.Reason })
            .IsUnique()
            .HasDatabaseName("uq_point_transactions_user_match_reason");

        // Per-group per-player history ordering.
        builder.HasIndex(t => new { t.GroupId, t.UserId, t.MatchDateTime })
            .HasDatabaseName("ix_point_transactions_group_user_date");

        // Per-match cleanup / lookup.
        builder.HasIndex(t => new { t.GroupId, t.MatchId })
            .HasDatabaseName("ix_point_transactions_group_match");
    }
}
