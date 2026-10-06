using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Gamification.Entities;

namespace Quadra.Modules.Gamification.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="GroupRanking"/> entity.
/// Maps to the <c>group_rankings</c> table with snake_case column names.
/// </summary>
public sealed class GroupRankingConfiguration : IEntityTypeConfiguration<GroupRanking>
{
    public void Configure(EntityTypeBuilder<GroupRanking> builder)
    {
        builder.ToTable("group_rankings");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.GroupId)
            .HasColumnName("group_id")
            .IsRequired();

        builder.Property(r => r.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(r => r.TotalPoints)
            .HasColumnName("total_points")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(r => r.MatchesCounted)
            .HasColumnName("matches_counted")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(r => r.LastMatchId)
            .HasColumnName("last_match_id");

        builder.Property(r => r.LastMatchDateTime)
            .HasColumnName("last_match_date_time");

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // One standing row per player per group.
        builder.HasIndex(r => new { r.GroupId, r.UserId })
            .IsUnique()
            .HasDatabaseName("uq_group_rankings_group_user");

        // Ranking ordering + deterministic tiebreak: total_points DESC, user_id ASC.
        builder.HasIndex(r => new { r.GroupId, r.TotalPoints, r.UserId })
            .IsDescending(false, true, false)
            .HasDatabaseName("ix_group_rankings_group_points");
    }
}
