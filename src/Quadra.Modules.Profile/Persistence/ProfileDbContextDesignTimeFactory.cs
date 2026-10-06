using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>
/// Design-time factory used exclusively by <c>dotnet ef</c> tooling to scaffold migrations without
/// booting the full host. Reads the connection string from the <c>ConnectionStrings__ProfileDb</c>
/// environment variable, falling back to the shared local dev default.
/// </summary>
public sealed class ProfileDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ProfileDbContext>
{
    public ProfileDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__ProfileDb")
            ?? "Host=localhost;Port=5432;Database=quadra;Username=quadra;Password=quadra";

        var optionsBuilder = new DbContextOptionsBuilder<ProfileDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.UseNetTopologySuite());

        return new ProfileDbContext(optionsBuilder.Options);
    }
}
