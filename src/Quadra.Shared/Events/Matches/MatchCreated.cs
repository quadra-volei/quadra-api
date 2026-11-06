namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published by the Matches module after a new match row is committed.
/// No handler today.
/// Handlers must be idempotent.
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
