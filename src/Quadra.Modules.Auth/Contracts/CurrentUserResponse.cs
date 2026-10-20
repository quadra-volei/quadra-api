namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// 200 OK body for <c>GET /api/v1/auth/me</c> — the account behind the presented access token.
/// Profile data (name, position, level) belongs to the Profile module, not here.
/// </summary>
public sealed record CurrentUserResponse(
    Guid UserId,
    string Provider,
    string? PhoneNumber,
    string? Email);
