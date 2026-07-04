namespace Quadra.Modules.Gamification.Entities;

/// <summary>
/// One immutable, append-only ledger row recording a single point award for a finished match.
/// It is both the audit trail and the idempotency guard for at-least-once SQS delivery: rows are
/// inserted idempotently on <c>(user_id, match_id, reason)</c> and never mutated. The materialized
/// <c>group_rankings</c> standing is recomputed (summed) from these rows, never incremented blindly.
/// </summary>
public sealed class PointTransaction
{
    // Private parameterless constructor required by EF Core.
    private PointTransaction() { }

    public Guid Id { get; private set; }

    /// <summary>The awarded player. App-layer FK to <c>users.id</c>; no DDL FK (module boundary rule).</summary>
    public Guid UserId { get; private set; }

    /// <summary>
    /// The grouping key. App-layer FK to the recurring <c>matches.id</c>. In the MVP
    /// <c>group_id == match_id</c>; kept distinct so a future recurring-occurrence model can repoint it.
    /// </summary>
    public Guid GroupId { get; private set; }

    /// <summary>The summarized match that triggered the award. App-layer FK to <c>matches.id</c>.</summary>
    public Guid MatchId { get; private set; }

    public PointReason Reason { get; private set; }

    public int Points { get; private set; }

    /// <summary>Snapshot of the triggering match's date/time; ordering key within a group.</summary>
    public DateTimeOffset MatchDateTime { get; private set; }

    /// <summary>The summary's <c>GeneratedAt</c>.</summary>
    public DateTimeOffset AwardedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Factory method — the only way to create a new ledger row. A row is immutable thereafter.</summary>
    public static PointTransaction Create(
        Guid userId,
        Guid groupId,
        Guid matchId,
        PointReason reason,
        int points,
        DateTimeOffset matchDateTime,
        DateTimeOffset awardedAt)
    {
        return new PointTransaction
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            GroupId = groupId,
            MatchId = matchId,
            Reason = reason,
            Points = points,
            MatchDateTime = matchDateTime,
            AwardedAt = awardedAt,
            // CreatedAt is left at its CLR default so the database applies its now() default.
        };
    }
}
