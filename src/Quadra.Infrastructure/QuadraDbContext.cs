using Microsoft.EntityFrameworkCore;

namespace Quadra.Infrastructure;

public class QuadraDbContext : DbContext
{
    public QuadraDbContext(DbContextOptions<QuadraDbContext> options) : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
    }
}
