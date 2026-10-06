using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// Fluent API configuration for the <see cref="TeamMember"/> entity.
/// Maps to the <c>team_members</c> table with snake_case column names.
/// </summary>
public sealed class TeamMemberConfiguration : IEntityTypeConfiguration<TeamMember>
{
    public void Configure(EntityTypeBuilder<TeamMember> builder)
    {
        builder.ToTable("team_members");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.IsGuest)
            .HasColumnName("is_guest")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(m => m.TeamId)
            .HasColumnName("team_id")
            .IsRequired();

        builder.Property(m => m.MatchId)
            .HasColumnName("match_id")
            .IsRequired();

        builder.Property(m => m.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired()
            .HasDefaultValueSql("now()");

        // Unique constraint: a player can only belong to one team per match.
        builder.HasIndex(m => new { m.MatchId, m.PlayerId })
            .IsUnique()
            .HasDatabaseName("uq_team_members_match_player");

        // Indexes
        builder.HasIndex(m => m.TeamId)
            .HasDatabaseName("ix_team_members_team_id");

        builder.HasIndex(m => m.MatchId)
            .HasDatabaseName("ix_team_members_match_id");
    }
}
