namespace Quadra.Shared.Events.InGame;

/// <summary>
/// Integration event published by the InGame module after an MVP voting session closes with a
/// decided MVP (at least one vote cast).
/// Handled in-process by: <c>ActivityConsumers</c> (friends' activity).
/// Handlers must be idempotent.
/// </summary>
public sealed record MvpAwarded(
    Guid MatchId,
    Guid PlayerId,
    int VoteCount,
    int TotalVotes,
    DateTimeOffset AwardedAt,
    DateTimeOffset OccurredAt);
