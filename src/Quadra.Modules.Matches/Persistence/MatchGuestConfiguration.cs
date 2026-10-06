using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>Fluent API mapping for <see cref="MatchGuest"/> (<c>match_guests</c>).</summary>
public sealed class MatchGuestConfiguration : IEntityTypeConfiguration<MatchGuest>
{
    public void Configure(EntityTypeBuilder<MatchGuest> builder)
    {
        builder.ToTable("match_guests");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(g => g.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(g => g.Name)
            .HasColumnName("name")
            .HasMaxLength(80)
            .IsRequired();

        builder.Property(g => g.Position)
            .HasColumnName("position")
            .HasMaxLength(8);

        builder.Property(g => g.AddedBy)
            .HasColumnName("added_by")
            .IsRequired();

        builder.Property(g => g.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.HasIndex(g => g.MatchId)
            .HasDatabaseName("ix_match_guests_match_id");

        builder.HasOne<Match>()
            .WithMany()
            .HasForeignKey(g => g.MatchId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
