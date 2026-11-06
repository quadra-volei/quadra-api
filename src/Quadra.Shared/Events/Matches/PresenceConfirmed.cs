namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published when a player successfully confirms their presence in a match.
/// No handler today.
/// Handlers must be idempotent.
/// </summary>
public sealed record PresenceConfirmed(
    Guid MatchId,
    Guid PlayerId,
    string PlayerType,
    DateTimeOffset ConfirmedAt,
    DateTimeOffset OccurredAt);
