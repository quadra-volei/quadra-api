namespace Quadra.Shared.Events.InGame;

/// <summary>
/// Integration event published by the InGame module after a successful team draft.
/// Declared consumers: <c>Quadra.Workers.Background</c> (F1.4 scoreboard init, F1.6 match summary).
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record TeamsFormed(
    Guid MatchId,
    IReadOnlyList<TeamFormedEntry> Teams,
    DateTimeOffset OccurredAt);
