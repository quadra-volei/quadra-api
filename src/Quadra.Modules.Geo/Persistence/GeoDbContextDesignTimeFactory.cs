using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Quadra.Modules.Geo.Persistence;

/// <summary>
/// Design-time factory used exclusively by <c>dotnet ef</c> tooling to scaffold migrations
/// without booting the full host. Reads the connection string from the
/// <c>ConnectionStrings__GeoDb</c> environment variable, falling back to the shared local dev default.
/// </summary>
public sealed class GeoDbContextDesignTimeFactory : IDesignTimeDbContextFactory<GeoDbContext>
{
    public GeoDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__GeoDb")
            ?? "Host=localhost;Port=5432;Database=quadra;Username=quadra;Password=quadra";

        var optionsBuilder = new DbContextOptionsBuilder<GeoDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite());

        return new GeoDbContext(optionsBuilder.Options);
    }
}
