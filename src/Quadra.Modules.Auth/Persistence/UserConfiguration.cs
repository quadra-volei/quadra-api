using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Persistence;

/// <summary>
/// Fluent API mapping for the <see cref="User"/> aggregate. Enforces the indexes and
/// length constraints, including one account per phone number and per external identity.
/// </summary>
public sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("users");

        builder.HasKey(u => u.Id);

        builder.Property(u => u.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        var providerConverter = new ValueConverter<IdentityProvider, string>(
            v => v.ToString().ToLowerInvariant(),
            v => ParseProvider(v));

        builder.Property(u => u.Provider)
            .HasColumnName("provider")
            .HasMaxLength(16)
            .HasConversion(providerConverter)
            .IsRequired();

        builder.Property(u => u.ExternalSubject)
            .HasColumnName("external_subject")
            .HasMaxLength(255);

        builder.Property(u => u.PhoneNumber)
            .HasColumnName("phone_number")
            .HasMaxLength(20);

        builder.Property(u => u.Email)
            .HasColumnName("email")
            .HasMaxLength(320);

        builder.Property(u => u.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(u => u.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.HasIndex(u => new { u.Provider, u.ExternalSubject })
            .IsUnique()
            .HasFilter("external_subject IS NOT NULL")
            .HasDatabaseName("ix_users_provider_external_subject");

        builder.HasIndex(u => u.PhoneNumber)
            .IsUnique()
            .HasFilter("phone_number IS NOT NULL")
            .HasDatabaseName("ix_users_phone_number");

        builder.HasIndex(u => u.Email)
            .HasDatabaseName("ix_users_email");

        builder.HasIndex(u => u.CreatedAt)
            .HasDatabaseName("ix_users_created_at");
    }

    private static IdentityProvider ParseProvider(string value)
    {
        return value switch
        {
            "phone" => IdentityProvider.Phone,
            "google" => IdentityProvider.Google,
            "apple" => IdentityProvider.Apple,
            _ => throw new InvalidOperationException(
                $"Unknown identity provider value '{value}' read from the database."),
        };
    }
}
