using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Persistence;

/// <summary>
/// EF Core DbContext owned by the Auth module. Only entities under <c>Quadra.Modules.Auth</c>
/// are mapped here; module-boundary rules forbid cross-module joins.
/// </summary>
public sealed class AuthDbContext : DbContext
{
    public AuthDbContext(DbContextOptions<AuthDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new UserConfiguration());
        modelBuilder.ApplyConfiguration(new RefreshTokenConfiguration());
    }
}
