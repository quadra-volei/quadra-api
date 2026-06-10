namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published by <c>CloseMatchWindowHandler</c> after transitioning match status to <c>Closed</c>
/// and releasing DropIn slots.
/// Declared consumers: <c>Quadra.Workers.Background</c> — notifies the organizer that the window closed
/// and dispatches push notification requests via <c>NotificationRequested</c>.
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record MatchWindowClosed(
    Guid MatchId,
    Guid OrganizerId,
    int ReleasedDropInSlots,
    DateTimeOffset OccurredAt);
