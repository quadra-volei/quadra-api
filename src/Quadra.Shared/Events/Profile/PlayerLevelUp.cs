namespace Quadra.Shared.Events.Profile;

/// <summary>
/// A player's level number went up after a finished match was applied to their stats.
/// Published by Profile; at-least-once, so consumers stay idempotent on (player, level).
/// </summary>
public sealed record PlayerLevelUp(
    Guid PlayerId,
    int FromLevel,
    int ToLevel,
    DateTimeOffset OccurredAt);
