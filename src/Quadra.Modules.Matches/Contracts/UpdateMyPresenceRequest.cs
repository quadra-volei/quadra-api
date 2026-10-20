namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Request body for <c>PUT /api/v1/matches/{id}/presences/me</c>.
/// </summary>
/// <param name="Status"><c>Confirmed | Declined</c>.</param>
/// <param name="InviteCode">The invite code, when joining a private match by code for the first time.</param>
public sealed record UpdateMyPresenceRequest(
    string Status,
    string? InviteCode = null);
