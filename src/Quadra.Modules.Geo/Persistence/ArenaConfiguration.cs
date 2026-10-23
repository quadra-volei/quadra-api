using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Geo.Arenas;
using Quadra.Modules.Geo.Venues;

namespace Quadra.Modules.Geo.Persistence;

/// <summary>
/// Maps <see cref="Arena"/> to <c>arenas</c>. The states are also checked by the database, so a
/// manual fix or a bug cannot leave a merged arena without a target or an approval without a reviewer.
/// </summary>
public sealed class ArenaConfiguration : IEntityTypeConfiguration<Arena>
{
    public void Configure(EntityTypeBuilder<Arena> builder)
    {
        builder.ToTable("arenas", t =>
        {
            t.HasCheckConstraint(
                "ck_arenas_kind",
                $"kind IN ('{VenueKinds.Court}', '{VenueKinds.Sand}', '{VenueKinds.Park}')");
            t.HasCheckConstraint(
                "ck_arenas_status",
                $"status IN ('{ArenaStatuses.Draft}', '{ArenaStatuses.Active}', '{ArenaStatuses.Inactive}', '{ArenaStatuses.Merged}')");
            t.HasCheckConstraint(
                "ck_arenas_merged_into",
                $"((status = '{ArenaStatuses.Merged}') = (merged_into_id IS NOT NULL)) AND merged_into_id IS DISTINCT FROM id");
            t.HasCheckConstraint(
                "ck_arenas_location_source",
                $"location_source IN ('{ArenaLocationSources.Curator}', '{ArenaLocationSources.Candidate}')");
            t.HasCheckConstraint(
                "ck_arenas_reviewed",
                $"status = '{ArenaStatuses.Draft}' OR (reviewed_by IS NOT NULL AND reviewed_at IS NOT NULL)");
        });

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(a => a.Location)
            .HasColumnName("location")
            .HasColumnType("geography (point, 4326)")
            .IsRequired();

        builder.Property(a => a.Kind)
            .HasColumnName("kind")
            .HasMaxLength(8)
            .IsRequired();

        builder.Property(a => a.LocationSource)
            .HasColumnName("location_source")
            .HasMaxLength(16)
            .HasDefaultValue(ArenaLocationSources.Curator)
            .IsRequired();

        builder.Property(a => a.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .HasDefaultValue(ArenaStatuses.Draft)
            .IsRequired();

        builder.Property(a => a.MergedIntoId)
            .HasColumnName("merged_into_id");

        builder.Property(a => a.ReviewedBy)
            .HasColumnName("reviewed_by");

        builder.Property(a => a.ReviewedAt)
            .HasColumnName("reviewed_at");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(a => a.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Arenas are never deleted (Inactive or Merged instead), so nothing cascades.
        builder.HasOne<Arena>()
            .WithMany()
            .HasForeignKey(a => a.MergedIntoId)
            .HasConstraintName("fk_arenas_arenas_merged_into_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(a => a.MergedIntoId)
            .HasDatabaseName("ix_arenas_merged_into_id");

        builder.HasIndex(a => a.Location)
            .HasMethod("gist")
            .HasDatabaseName("ix_arenas_location_gist");

        builder.HasIndex(a => a.Status)
            .HasDatabaseName("ix_arenas_status");
    }
}

/// <summary>Maps <see cref="ArenaPlace"/> to <c>arena_places</c>. No foreign key to <c>venues</c>: its purge must not touch these rows.</summary>
public sealed class ArenaPlaceConfiguration : IEntityTypeConfiguration<ArenaPlace>
{
    public void Configure(EntityTypeBuilder<ArenaPlace> builder)
    {
        builder.ToTable("arena_places", t =>
        {
            t.HasCheckConstraint(
                "ck_arena_places_status",
                $"(status = '{ArenaPlaceStatuses.Linked}' AND arena_id IS NOT NULL) OR (status = '{ArenaPlaceStatuses.Rejected}' AND arena_id IS NULL)");
            t.HasCheckConstraint(
                "ck_arena_places_primary",
                $"NOT is_primary OR status = '{ArenaPlaceStatuses.Linked}'");
        });

        builder.HasKey(p => p.PlaceId);
        builder.Property(p => p.PlaceId)
            .HasColumnName("place_id")
            .HasMaxLength(255);

        builder.Property(p => p.ArenaId)
            .HasColumnName("arena_id");

        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasMaxLength(16)
            .IsRequired();

        builder.Property(p => p.IsPrimary)
            .HasColumnName("is_primary")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.ReviewedBy)
            .HasColumnName("reviewed_by")
            .IsRequired();

        builder.Property(p => p.ReviewedAt)
            .HasColumnName("reviewed_at")
            .IsRequired();

        builder.HasOne<Arena>()
            .WithMany()
            .HasForeignKey(p => p.ArenaId)
            .HasConstraintName("fk_arena_places_arenas_arena_id")
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(p => p.ArenaId, "ix_arena_places_arena_id");

        // One main entry per arena.
        builder.HasIndex(p => p.ArenaId, "uq_arena_places_primary")
            .IsUnique()
            .HasFilter("is_primary");
    }
}
