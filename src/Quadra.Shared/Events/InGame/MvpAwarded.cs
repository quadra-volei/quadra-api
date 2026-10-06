namespace Quadra.Shared.Events.InGame;

/// <summary>
/// Integration event published by the InGame module after an MVP voting session closes with a
/// decided MVP (at least one vote cast).
/// Declared consumers: <c>Quadra.Workers.Background</c> (distributes the +25 MVP points via
/// Gamification, triggers Profile stats updates and enqueues an in-app "MVP awarded" notification).
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record MvpAwarded(
    Guid MatchId,
    Guid PlayerId,
    int VoteCount,
    int TotalVotes,
    DateTimeOffset AwardedAt,
    DateTimeOffset OccurredAt);
