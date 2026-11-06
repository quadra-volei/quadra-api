namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published by the Matches module after a status transition (e.g. cancellation).
/// No handler today.
/// Handlers must be idempotent.
/// </summary>
public sealed record MatchStatusChanged(
    Guid MatchId,
    Guid OrganizerId,
    string PreviousStatus,
    string NewStatus,
    DateTimeOffset OccurredAt);
