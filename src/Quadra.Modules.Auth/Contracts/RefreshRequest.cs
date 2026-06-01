namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/auth/refresh</c>. The refresh token is itself the credential;
/// no access token is required.
/// </summary>
public sealed record RefreshRequest(
    string RefreshToken,
    string? DeviceId);
