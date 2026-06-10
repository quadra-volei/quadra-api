namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Request body for <c>PUT /api/v1/matches/{id}/presences/me</c>.
/// </summary>
public sealed record UpdateMyPresenceRequest(
    string Status); // "Confirmed" | "Declined"
