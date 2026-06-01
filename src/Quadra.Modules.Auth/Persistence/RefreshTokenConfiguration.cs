using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Persistence;

/// <summary>
/// Fluent API mapping for the <see cref="RefreshToken"/> entity. snake_case columns, the indexes
/// described in the FA.3 spec, and an intra-module FK to <c>users.id</c>.
/// </summary>
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("refresh_tokens");

        builder.HasKey(t => t.Id);

        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.UserId)
            .HasColumnName("user_id")
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .HasColumnName("token_hash")
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(t => t.CognitoSub)
            .HasColumnName("cognito_sub")
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(t => t.DeviceId)
            .HasColumnName("device_id")
            .HasMaxLength(128);

        builder.Property(t => t.IssuedAt)
            .HasColumnName("issued_at")
            .HasDefaultValueSql("now()")
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(t => t.RevokedAt)
            .HasColumnName("revoked_at");

        builder.HasIndex(t => t.TokenHash)
            .IsUnique()
            .HasDatabaseName("ix_refresh_tokens_token_hash");

        builder.HasIndex(t => t.UserId)
            .HasDatabaseName("ix_refresh_tokens_user_id");

        builder.HasIndex(t => t.CognitoSub)
            .HasDatabaseName("ix_refresh_tokens_cognito_sub");

        builder.HasIndex(t => t.ExpiresAt)
            .HasDatabaseName("ix_refresh_tokens_expires_at");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
