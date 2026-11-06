namespace Quadra.Shared.Events.InGame;

/// <summary>
/// Integration event published by the InGame module after a scoreboard transitions to InProgress.
/// Handled in-process by: <c>ActivityConsumers</c> (friends' activity).
/// Handlers must be idempotent.
/// </summary>
public sealed record MatchStarted(
    Guid MatchId,
    Guid TeamAId,
    Guid TeamBId,
    string Format,
    DateTimeOffset StartedAt,
    DateTimeOffset OccurredAt);
