namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published by the Matches module after a new match row is committed.
/// Declared consumers: <c>Quadra.Modules.Notifications</c>, <c>Quadra.Workers.Background</c>.
/// At-least-once delivery — consumers must be idempotent.
/// </summary>
public sealed record MatchCreated(
    Guid MatchId,
    Guid OrganizerId,
    string Name,
    string Type,
    DateTimeOffset DateTime,
    DateTimeOffset WindowOpensAt,
    DateTimeOffset WindowClosesAt,
    int MaxPlayers,
    int RegularSlots,
    int DropInSlots,
    DateTimeOffset OccurredAt);
