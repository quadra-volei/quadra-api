using Microsoft.EntityFrameworkCore;

namespace Quadra.Modules.Geo.Persistence;

/// <summary>
/// Read-only EF Core DbContext for the Geo module. Maps only the Matches-owned <c>matches</c>
/// table (via <see cref="NearbyMatchRecord"/>) under the ARCHITECTURE-sanctioned SELECT grant.
/// Geo owns no tables and emits no migrations; all queries are read-only.
/// </summary>
public sealed class GeoReadDbContext : DbContext
{
    public GeoReadDbContext(DbContextOptions<GeoReadDbContext> options)
        : base(options)
    {
    }

    public DbSet<NearbyMatchRecord> Matches => Set<NearbyMatchRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new NearbyMatchConfiguration());
    }
}
