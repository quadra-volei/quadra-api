using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="PlayerCard"/> entity.
/// Maps to the <c>player_cards</c> table with snake_case column names.
/// </summary>
public sealed class PlayerCardConfiguration : IEntityTypeConfiguration<PlayerCard>
{
    public void Configure(EntityTypeBuilder<PlayerCard> builder)
    {
        builder.ToTable("player_cards");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(c => c.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(c => c.Position)
            .HasColumnName("position")
            .HasConversion<string>()
            .HasMaxLength(8);

        builder.Property(c => c.Level)
            .HasColumnName("level")
            .HasConversion<string>()
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(c => c.MatchesPlayed)
            .HasColumnName("matches_played")
            .IsRequired();

        builder.Property(c => c.Wins)
            .HasColumnName("wins")
            .IsRequired();

        builder.Property(c => c.Losses)
            .HasColumnName("losses")
            .IsRequired();

        builder.Property(c => c.Draws)
            .HasColumnName("draws")
            .IsRequired();

        builder.Property(c => c.MvpsReceived)
            .HasColumnName("mvps_received")
            .IsRequired();

        builder.Property(c => c.GeneratedAt)
            .HasColumnName("generated_at")
            .IsRequired();

        builder.Property(c => c.RefreshedAt)
            .HasColumnName("refreshed_at")
            .IsRequired();

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // One card per user.
        builder.HasIndex(c => c.UserId)
            .IsUnique()
            .HasDatabaseName("uq_player_cards_user_id");
    }
}
