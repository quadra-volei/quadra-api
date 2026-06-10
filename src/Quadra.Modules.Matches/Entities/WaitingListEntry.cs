namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Represents a player's position in the waiting list for a given match.
/// One row per (match, player) pair — enforced by a unique constraint in the database.
/// </summary>
public sealed class WaitingListEntry
{
    // Private parameterless constructor required by EF Core.
    private WaitingListEntry() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid MatchId { get; private set; }

    /// <summary>App-layer FK to <c>users.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid PlayerId { get; private set; }

    public PlayerType PlayerType { get; private set; }

    /// <summary>
    /// 1-based position within the waiting list for a given match. Lower = higher priority.
    /// </summary>
    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Factory method — the only way to create a new <see cref="WaitingListEntry"/> instance.
    /// </summary>
    public static WaitingListEntry Create(
        Guid matchId,
        Guid playerId,
        PlayerType playerType,
        int position,
        DateTimeOffset now)
    {
        return new WaitingListEntry
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            PlayerId = playerId,
            PlayerType = playerType,
            Position = position,
            CreatedAt = now,
        };
    }
}
