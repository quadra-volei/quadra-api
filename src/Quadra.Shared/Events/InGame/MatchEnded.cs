namespace Quadra.Shared.Events.InGame;

/// <summary>
/// Integration event published by the InGame module after a scoreboard transitions to Ended,
/// whether by natural completion or a forced early end.
/// Declared consumers: <c>Quadra.Workers.Background</c> (distributes win points via Gamification,
/// triggers Profile stats updates and feeds the F1.6 match summary).
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record MatchEnded(
    Guid MatchId,
    Guid TeamAId,
    Guid TeamBId,
    int TeamASetsWon,
    int TeamBSetsWon,
    Guid? WinnerTeamId,
    IReadOnlyList<MatchEndedSetResult> Sets,
    DateTimeOffset EndedAt,
    DateTimeOffset OccurredAt);
