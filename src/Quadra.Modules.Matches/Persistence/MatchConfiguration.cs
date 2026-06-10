using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="Match"/> entity.
/// Maps to the <c>matches</c> table with snake_case column names.
/// </summary>
public sealed class MatchConfiguration : IEntityTypeConfiguration<Match>
{
    public void Configure(EntityTypeBuilder<Match> builder)
    {
        builder.ToTable("matches");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.OrganizerId)
            .HasColumnName("organizer_id")
            .IsRequired();

        builder.Property(m => m.Name)
            .HasColumnName("name")
            .HasMaxLength(120)
            .IsRequired();

        builder.Property(m => m.Description)
            .HasColumnName("description");

        builder.Property(m => m.Address)
            .HasColumnName("address")
            .HasMaxLength(300)
            .IsRequired();

        builder.Property(m => m.Location)
            .HasColumnName("location")
            .HasColumnType("geography (point, 4326)")
            .IsRequired();

        builder.Property(m => m.DateTime)
            .HasColumnName("date_time")
            .IsRequired();

        builder.Property(m => m.MaxPlayers)
            .HasColumnName("max_players")
            .IsRequired();

        builder.Property(m => m.RegularSlots)
            .HasColumnName("regular_slots")
            .IsRequired();

        // Computed column: max_players - regular_slots. EF never writes this column.
        builder.Property(m => m.DropInSlots)
            .HasColumnName("drop_in_slots")
            .HasComputedColumnSql("max_players - regular_slots", stored: true);

        builder.Property(m => m.Price)
            .HasColumnName("price")
            .HasColumnType("numeric(10,2)");

        builder.Property(m => m.Type)
            .HasColumnName("type")
            .HasMaxLength(16)
            .IsRequired()
            .HasConversion<string>();

        builder.Property(m => m.Frequency)
            .HasColumnName("frequency")
            .HasMaxLength(16)
            .HasConversion<string?>();

        builder.Property(m => m.DayOfWeek)
            .HasColumnName("day_of_week");

        builder.Property(m => m.WindowOpensAt)
            .HasColumnName("window_opens_at")
            .IsRequired();

        builder.Property(m => m.WindowClosesAt)
            .HasColumnName("window_closes_at")
            .IsRequired();

        builder.Property(m => m.Status)
            .HasColumnName("status")
            .HasMaxLength(24)
            .IsRequired()
            .HasConversion<string>()
            .HasDefaultValue(MatchStatus.Draft);

        builder.Property(m => m.ReleasedDropInSlots)
            .HasColumnName("released_drop_in_slots");

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(m => m.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Indexes
        builder.HasIndex(m => m.OrganizerId)
            .HasDatabaseName("ix_matches_organizer_id");

        builder.HasIndex(m => m.DateTime)
            .HasDatabaseName("ix_matches_date_time");

        builder.HasIndex(m => m.Status)
            .HasDatabaseName("ix_matches_status");

        // GIST index for PostGIS spatial queries (required by F1.7).
        builder.HasIndex(m => m.Location)
            .HasMethod("gist")
            .HasDatabaseName("ix_matches_location_gist");

        builder.HasIndex(m => new { m.Type, m.Status })
            .HasDatabaseName("ix_matches_type_status");
    }
}
