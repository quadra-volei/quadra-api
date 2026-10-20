namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/auth/logout</c>. The refresh token identifies the session
/// to end.
/// </summary>
public sealed record LogoutRequest(string RefreshToken);
