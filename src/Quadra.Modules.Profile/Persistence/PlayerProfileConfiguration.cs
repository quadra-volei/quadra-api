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

        // Derived from the name fields; not a column.
        builder.Ignore(p => p.DisplayName);
        builder.Ignore(p => p.IsOnboardingCompleted);

        builder.Property(p => p.FirstName)
            .HasColumnName("first_name")
            .HasMaxLength(40)
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.Property(p => p.LastName)
            .HasColumnName("last_name")
            .HasMaxLength(40)
            .HasDefaultValue(string.Empty)
            .IsRequired();

        builder.Property(p => p.Handle)
            .HasColumnName("handle")
            .HasMaxLength(20);

        builder.Property(p => p.BirthDate)
            .HasColumnName("birth_date");

        builder.Property(p => p.Position)
            .HasColumnName("position")
            .HasConversion<string>()
            .HasMaxLength(8);

        builder.Property(p => p.PreferredModality)
            .HasColumnName("preferred_modality")
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(p => p.DeclaredLevel)
            .HasColumnName("declared_level")
            .HasConversion<string>()
            .HasMaxLength(16);

        builder.Property(p => p.OnboardingCompletedAt)
            .HasColumnName("onboarding_completed_at");

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

        // Handles are stored lowercase, so a plain unique index makes them case-insensitively unique.
        builder.HasIndex(p => p.Handle)
            .IsUnique()
            .HasFilter("handle IS NOT NULL")
            .HasDatabaseName("uq_player_profiles_handle");
    }
}
