namespace Quadra.Shared.Events.InGame;

/// <summary>
/// Integration event published by the InGame module after a scoreboard transitions to InProgress.
/// Declared consumers: <c>Quadra.Workers.Background</c> (records match start time for the F1.6 summary
/// and enqueues an in-app "match started" notification request).
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record MatchStarted(
    Guid MatchId,
    Guid TeamAId,
    Guid TeamBId,
    string Format,
    DateTimeOffset StartedAt,
    DateTimeOffset OccurredAt);
