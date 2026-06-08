namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published by the Matches module after a status transition (e.g. cancellation).
/// Declared consumers: <c>Quadra.Modules.Notifications</c>.
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record MatchStatusChanged(
    Guid MatchId,
    Guid OrganizerId,
    string PreviousStatus,
    string NewStatus,
    DateTimeOffset OccurredAt);
