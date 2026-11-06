namespace Quadra.Shared.Events.InGame;

/// <summary>
/// Integration event published by the InGame module after a successful team draft.
/// No handler today.
/// Handlers must be idempotent.
/// </summary>
public sealed record TeamsFormed(
    Guid MatchId,
    IReadOnlyList<TeamFormedEntry> Teams,
    DateTimeOffset OccurredAt);
