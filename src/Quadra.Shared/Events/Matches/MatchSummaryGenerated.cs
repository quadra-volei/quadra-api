namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published by the Matches module after the immutable match summary is
/// persisted (F1.6). This is the canonical "match fully completed" signal.
/// Declared consumers: <c>Quadra.Workers.Background</c> — enqueues an in-app "summary available"
/// notification for each participant and lets <c>Profile</c> finalize each participant's match
/// history entry (F2.1).
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record MatchSummaryGenerated(
    Guid MatchId,
    Guid WinnerTeamId,          // Guid.Empty when the game ended level (no winner)
    Guid? MvpPlayerId,
    int DurationSeconds,
    IReadOnlyList<Guid> ParticipantPlayerIds,
    DateTimeOffset GeneratedAt,
    DateTimeOffset OccurredAt);
