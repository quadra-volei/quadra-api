using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Geo.Venues;

namespace Quadra.Modules.Geo.Persistence;

/// <summary>Maps <see cref="Venue"/> to <c>venues</c>.</summary>
public sealed class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("venues");

        builder.HasKey(v => v.Id);
        builder.Property(v => v.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(v => v.PlaceId)
            .HasColumnName("place_id")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(v => v.Location)
            .HasColumnName("location")
            .HasColumnType("geography (point, 4326)")
            .IsRequired();

        builder.Property(v => v.FetchedAt)
            .HasColumnName("fetched_at")
            .IsRequired();

        builder.Property(v => v.Kind)
            .HasColumnName("kind")
            .HasMaxLength(8)
            .HasDefaultValue(VenueKinds.Court)
            .IsRequired();

        builder.HasIndex(v => v.PlaceId)
            .IsUnique()
            .HasDatabaseName("uq_venues_place_id");

        builder.HasIndex(v => v.Location)
            .HasMethod("gist")
            .HasDatabaseName("ix_venues_location_gist");

        builder.HasIndex(v => v.FetchedAt)
            .HasDatabaseName("ix_venues_fetched_at");
    }
}

/// <summary>Maps <see cref="VenueArea"/> to <c>venue_areas</c>.</summary>
public sealed class VenueAreaConfiguration : IEntityTypeConfiguration<VenueArea>
{
    public void Configure(EntityTypeBuilder<VenueArea> builder)
    {
        builder.ToTable("venue_areas");

        builder.HasKey(a => a.Key);
        builder.Property(a => a.Key)
            .HasColumnName("key")
            .HasMaxLength(32);

        builder.Property(a => a.DiscoveredAt)
            .HasColumnName("discovered_at")
            .IsRequired();
    }
}
