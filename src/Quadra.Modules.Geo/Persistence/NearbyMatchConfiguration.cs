using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Quadra.Modules.Geo.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="NearbyMatchRecord"/> read projection.
/// Maps only the columns Geo is permitted to read on the Matches-owned <c>matches</c> table.
/// The table is excluded from migrations — its DDL is owned by the Matches module (F1.1).
/// </summary>
public sealed class NearbyMatchConfiguration : IEntityTypeConfiguration<NearbyMatchRecord>
{
    public void Configure(EntityTypeBuilder<NearbyMatchRecord> builder)
    {
        builder.ToTable("matches", t => t.ExcludeFromMigrations());

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id");

        builder.Property(m => m.Name)
            .HasColumnName("name");

        builder.Property(m => m.Address)
            .HasColumnName("address");

        builder.Property(m => m.Location)
            .HasColumnName("location")
            .HasColumnType("geography (point, 4326)");

        builder.Property(m => m.DateTime)
            .HasColumnName("date_time");

        builder.Property(m => m.MaxPlayers)
            .HasColumnName("max_players");

        builder.Property(m => m.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(10,2)");

        builder.Property(m => m.Type)
            .HasColumnName("type");

        builder.Property(m => m.Status)
            .HasColumnName("status");
    }
}
