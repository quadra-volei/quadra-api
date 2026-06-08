using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql.EntityFrameworkCore.PostgreSQL.NetTopologySuite;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Design-time factory used exclusively by <c>dotnet ef</c> tooling to scaffold migrations
/// without booting the full host.
/// Reads the connection string from the <c>ConnectionStrings__MatchesDb</c> environment variable,
/// falling back to the shared local dev default.
/// </summary>
public sealed class MatchesDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MatchesDbContext>
{
    public MatchesDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__MatchesDb")
            ?? "Host=localhost;Port=5432;Database=quadra;Username=quadra;Password=quadra";

        var optionsBuilder = new DbContextOptionsBuilder<MatchesDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.UseNetTopologySuite());

        return new MatchesDbContext(optionsBuilder.Options);
    }
}
