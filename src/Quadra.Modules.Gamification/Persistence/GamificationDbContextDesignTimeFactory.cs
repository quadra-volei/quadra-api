using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Quadra.Modules.Gamification.Persistence;

/// <summary>
/// Design-time factory used exclusively by <c>dotnet ef</c> tooling to scaffold migrations without
/// booting the full host. Reads the connection string from the <c>ConnectionStrings__GamificationDb</c>
/// environment variable, falling back to the shared local dev default.
/// </summary>
public sealed class GamificationDbContextDesignTimeFactory
    : IDesignTimeDbContextFactory<GamificationDbContext>
{
    public GamificationDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__GamificationDb")
            ?? "Host=localhost;Port=5432;Database=quadra;Username=quadra;Password=quadra";

        var optionsBuilder = new DbContextOptionsBuilder<GamificationDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new GamificationDbContext(optionsBuilder.Options);
    }
}
