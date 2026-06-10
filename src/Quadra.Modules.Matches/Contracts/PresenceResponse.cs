namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Read model representing a single player's presence record.
/// </summary>
public sealed record PresenceResponse(
    Guid Id,
    Guid MatchId,
    Guid PlayerId,
    string PlayerType,
    string Status,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
