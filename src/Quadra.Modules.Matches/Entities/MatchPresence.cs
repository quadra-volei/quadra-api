namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Represents a player's presence record for a given match.
/// One row per (match, player) pair — enforced by a unique constraint in the database.
/// All state changes go through explicit methods to enforce invariants.
/// </summary>
public sealed class MatchPresence
{
    // Private parameterless constructor required by EF Core.
    private MatchPresence() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid MatchId { get; private set; }

    /// <summary>App-layer FK to <c>users.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid PlayerId { get; private set; }

    public PlayerType PlayerType { get; private set; }
    public PresenceStatus Status { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Factory method — the only way to create a new <see cref="MatchPresence"/> instance.
    /// The initial status is always <see cref="PresenceStatus.Pending"/>.
    /// </summary>
    public static MatchPresence Create(
        Guid matchId,
        Guid playerId,
        PlayerType playerType,
        DateTimeOffset now)
    {
        return new MatchPresence
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            PlayerId = playerId,
            PlayerType = playerType,
            Status = PresenceStatus.Pending,
            ConfirmedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Transitions the presence to <see cref="PresenceStatus.Confirmed"/>.
    /// Sets <see cref="ConfirmedAt"/> and <see cref="UpdatedAt"/> to <paramref name="now"/>.
    /// </summary>
    public void Confirm(DateTimeOffset now)
    {
        Status = PresenceStatus.Confirmed;
        ConfirmedAt = now;
        UpdatedAt = now;
    }

    /// <summary>
    /// Transitions the presence to <see cref="PresenceStatus.Declined"/>.
    /// Sets <see cref="UpdatedAt"/> to <paramref name="now"/>.
    /// </summary>
    public void Decline(DateTimeOffset now)
    {
        Status = PresenceStatus.Declined;
        UpdatedAt = now;
    }
}
