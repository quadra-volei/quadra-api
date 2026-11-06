namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published by <c>OpenMatchWindowHandler</c> after transitioning match status to <c>Open</c>.
/// No handler today.
/// Handlers must be idempotent.
/// </summary>
public sealed record MatchWindowOpened(
    Guid MatchId,
    Guid OrganizerId,
    DateTimeOffset WindowClosesAt,
    IReadOnlyList<Guid> PendingPlayerIds,
    DateTimeOffset OccurredAt);
