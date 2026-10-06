namespace Quadra.Modules.InGame.Entities;

/// <summary>
/// Represents a player assigned to a specific team within a match.
/// <c>match_id</c> is denormalized to enable the UNIQUE (match_id, player_id) constraint
/// and efficient per-match queries without joining to <c>teams</c>.
/// </summary>
public sealed class TeamMember
{
    // Private parameterless constructor required by EF Core.
    private TeamMember() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>teams.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid TeamId { get; private set; }

    /// <summary>
    /// Denormalized app-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).
    /// Enables the UNIQUE (match_id, player_id) constraint without joining to <c>teams</c>.
    /// </summary>
    public Guid MatchId { get; private set; }

    /// <summary>App-layer FK to <c>users.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid PlayerId { get; private set; }

    /// <summary>
    /// True when <see cref="PlayerId"/> is a guest of the match (a player without an account)
    /// instead of a user: guests play, but have no profile, no vote and no stats.
    /// </summary>
    public bool IsGuest { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Factory method — the only way to create a new <see cref="TeamMember"/> instance.
    /// </summary>
    public static TeamMember Create(
        Guid teamId,
        Guid matchId,
        Guid playerId,
        DateTimeOffset now,
        bool isGuest = false)
    {
        return new TeamMember
        {
            Id = Guid.NewGuid(),
            TeamId = teamId,
            MatchId = matchId,
            PlayerId = playerId,
            IsGuest = isGuest,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Moves this member to a different team by updating <see cref="TeamId"/>.
    /// No <c>updated_at</c> column exists on <c>team_members</c>.
    /// </summary>
    public void MoveTo(Guid newTeamId)
    {
        TeamId = newTeamId;
    }
}
