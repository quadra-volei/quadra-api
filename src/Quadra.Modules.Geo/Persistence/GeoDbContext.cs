using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Geo.Venues;

namespace Quadra.Modules.Geo.Persistence;

/// <summary>
/// EF Core DbContext for the Geo module. Reads the Matches-owned <c>matches</c> table (via
/// <see cref="NearbyMatchRecord"/>, excluded from migrations) under the ARCHITECTURE-sanctioned
/// SELECT grant, and owns the arena cache: <c>venues</c> and <c>venue_areas</c>.
/// </summary>
public sealed class GeoDbContext : DbContext
{
    public GeoDbContext(DbContextOptions<GeoDbContext> options)
        : base(options)
    {
    }

    public DbSet<NearbyMatchRecord> Matches => Set<NearbyMatchRecord>();

    public DbSet<Venue> Venues => Set<Venue>();

    public DbSet<VenueArea> VenueAreas => Set<VenueArea>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfiguration(new NearbyMatchConfiguration());
        modelBuilder.ApplyConfiguration(new VenueConfiguration());
        modelBuilder.ApplyConfiguration(new VenueAreaConfiguration());
    }
}
