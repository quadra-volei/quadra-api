namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published when a player successfully confirms their presence in a match.
/// Declared consumers: <c>Quadra.Modules.Notifications</c> — stores an in-app notification record for the player.
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record PresenceConfirmed(
    Guid MatchId,
    Guid PlayerId,
    string PlayerType,
    DateTimeOffset ConfirmedAt,
    DateTimeOffset OccurredAt);
