using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="PlayerProfile"/> entity.
/// Maps to the <c>player_profiles</c> table with snake_case column names.
/// </summary>
public sealed class PlayerProfileConfiguration : IEntityTypeConfiguration<PlayerProfile>
{
    public void Configure(EntityTypeBuilder<PlayerProfile> builder)
    {
        builder.ToTable("player_profiles");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(p => p.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(p => p.PrimaryPosition)
            .HasColumnName("primary_position")
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(p => p.SecondaryPosition)
            .HasColumnName("secondary_position")
            .HasConversion<string>()
            .HasMaxLength(24);

        builder.Property(p => p.PhotoObjectKey)
            .HasColumnName("photo_object_key")
            .HasMaxLength(512);

        builder.Property(p => p.Level)
            .HasColumnName("level")
            .HasConversion<string>()
            .HasMaxLength(16)
            .HasDefaultValue(PlayerLevel.Beginner)
            .IsRequired();

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // One profile per user.
        builder.HasIndex(p => p.UserId)
            .IsUnique()
            .HasDatabaseName("uq_player_profiles_user_id");
    }
}
