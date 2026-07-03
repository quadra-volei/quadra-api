using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="PlayerStats"/> entity.
/// Maps to the <c>player_stats</c> table with snake_case column names.
/// </summary>
public sealed class PlayerStatsConfiguration : IEntityTypeConfiguration<PlayerStats>
{
    public void Configure(EntityTypeBuilder<PlayerStats> builder)
    {
        builder.ToTable("player_stats");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(s => s.MatchesPlayed)
            .HasColumnName("matches_played")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.Wins)
            .HasColumnName("wins")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.Losses)
            .HasColumnName("losses")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.Draws)
            .HasColumnName("draws")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.MvpsReceived)
            .HasColumnName("mvps_received")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(s => s.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // One stats row per user.
        builder.HasIndex(s => s.UserId)
            .IsUnique()
            .HasDatabaseName("uq_player_stats_user_id");
    }
}
