using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="Team"/> entity.
/// Maps to the <c>teams</c> table with snake_case column names.
/// </summary>
public sealed class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> builder)
    {
        builder.ToTable("teams");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(t => t.Name)
            .HasColumnName("name")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: team name is unique within a match.
        builder.HasIndex(t => new { t.MatchId, t.Name })
            .IsUnique()
            .HasDatabaseName("uq_teams_match_name");

        // Index on match_id for per-match queries.
        builder.HasIndex(t => t.MatchId)
            .HasDatabaseName("ix_teams_match_id");

        // Members is an in-memory collection not mapped as an EF navigation.
        // The repository loads members in a separate query and attaches them via AttachMembers.
        // This avoids generating a DDL FK constraint (module boundary rule applies within module too).
        builder.Ignore(t => t.Members);
    }
}
