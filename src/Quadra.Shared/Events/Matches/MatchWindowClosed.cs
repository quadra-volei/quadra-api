namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published by <c>CloseMatchWindowHandler</c> after transitioning match status to <c>Closed</c>
/// and releasing DropIn slots.
/// No handler today.
/// Handlers must be idempotent.
/// </summary>
public sealed record MatchWindowClosed(
    Guid MatchId,
    Guid OrganizerId,
    int ReleasedDropInSlots,
    DateTimeOffset OccurredAt);
