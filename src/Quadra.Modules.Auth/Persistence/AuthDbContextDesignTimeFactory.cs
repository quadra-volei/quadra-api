using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Quadra.Modules.Auth.Persistence;

/// <summary>
/// Design-time factory used exclusively by <c>dotnet ef</c> tooling to scaffold migrations
/// without booting the full host. Points at a placeholder local connection string;
/// the runtime configuration is the only one actually applied to a real database.
/// </summary>
public sealed class AuthDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AuthDbContext>
{
    public AuthDbContext CreateDbContext(string[] args)
    {
        var builder = new DbContextOptionsBuilder<AuthDbContext>();
        builder.UseNpgsql("Host=localhost;Database=quadra_design;Username=postgres;Password=postgres");
        return new AuthDbContext(builder.Options);
    }
}
