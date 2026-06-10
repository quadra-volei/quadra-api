namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published by <c>OpenMatchWindowHandler</c> after transitioning match status to <c>Open</c>.
/// Declared consumers: <c>Quadra.Workers.Background</c> — dispatches push notification requests for each player
/// in <see cref="PendingPlayerIds"/> by publishing <c>NotificationRequested</c> events to the Notification Worker.
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record MatchWindowOpened(
    Guid MatchId,
    Guid OrganizerId,
    DateTimeOffset WindowClosesAt,
    IReadOnlyList<Guid> PendingPlayerIds,
    DateTimeOffset OccurredAt);
