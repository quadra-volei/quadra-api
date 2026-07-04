namespace Quadra.Modules.Gamification.Entities;

/// <summary>
/// Materialized standing of one player inside one group. Recomputed from <c>point_transactions</c>
/// on every finished match; this is what the read endpoint serves. One row per player per group.
/// </summary>
public sealed class GroupRanking
{
    // Private parameterless constructor required by EF Core.
    private GroupRanking() { }

    public Guid Id { get; private set; }

    /// <summary>The recurring <c>matches.id</c> grouping key. App-layer FK; kept distinct from match_id.</summary>
    public Guid GroupId { get; private set; }

    /// <summary>App-layer FK to <c>users.id</c>; no DDL FK (module boundary rule).</summary>
    public Guid UserId { get; private set; }

    /// <summary>Sum of this player's <c>point_transactions.points</c> for this group.</summary>
    public int TotalPoints { get; private set; }

    /// <summary>Distinct summarized matches in the group this player attended.</summary>
    public int MatchesCounted { get; private set; }

    public Guid? LastMatchId { get; private set; }

    public DateTimeOffset? LastMatchDateTime { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Factory for a fresh, zeroed standing row.</summary>
    public static GroupRanking Empty(Guid groupId, Guid userId, DateTimeOffset now)
    {
        return new GroupRanking
        {
            Id = Guid.NewGuid(),
            GroupId = groupId,
            UserId = userId,
            TotalPoints = 0,
            MatchesCounted = 0,
            LastMatchId = null,
            LastMatchDateTime = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Overwrites the recomputed standing values (recompute-not-increment).</summary>
    public void Set(
        int totalPoints,
        int matchesCounted,
        Guid? lastMatchId,
        DateTimeOffset? lastMatchDateTime,
        DateTimeOffset now)
    {
        TotalPoints = totalPoints;
        MatchesCounted = matchesCounted;
        LastMatchId = lastMatchId;
        LastMatchDateTime = lastMatchDateTime;
        UpdatedAt = now;
    }
}
