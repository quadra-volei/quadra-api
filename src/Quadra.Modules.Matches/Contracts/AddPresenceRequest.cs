namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/matches/{id}/presences</c>.
/// </summary>
public sealed record AddPresenceRequest(
    Guid PlayerId,
    string PlayerType); // "Regular" | "DropIn"
