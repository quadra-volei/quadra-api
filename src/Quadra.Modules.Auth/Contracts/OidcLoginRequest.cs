namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/auth/login/google</c> and <c>POST /api/v1/auth/login/apple</c>.
/// <see cref="IdToken"/> is the provider ID token obtained by the mobile client from the native SDK.
/// </summary>
public sealed record OidcLoginRequest(
    string IdToken,
    string? DeviceId);
