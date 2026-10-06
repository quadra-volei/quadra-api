namespace Quadra.Modules.Gamification.Abstractions;

/// <summary>
/// Gamification-owned write interface that applies a finished recurring match's point awards: inserts
/// the immutable ledger rows and recomputes the affected players' <c>group_rankings</c> standings.
/// Invoked by the Background Worker when it consumes <c>MatchSummaryGenerated</c>. Idempotent under
/// at-least-once delivery (UNIQUE <c>(user_id, match_id, reason)</c>; recompute-not-increment).
/// </summary>
public interface IMatchPointsWriter
{
    Task ApplyFinishedMatchPointsAsync(FinishedMatchPoints points, CancellationToken cancellationToken);
}
