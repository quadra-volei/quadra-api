namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// Shared 200 OK body for every token-issuing leg (SMS OTP verify, Google, Apple, refresh).
/// <see cref="AccessToken"/> is a JWT signed by this API; <see cref="ExpiresIn"/> is its lifetime
/// in seconds. The raw <see cref="RefreshToken"/> is returned to the client exactly once per
/// issuance and is never persisted (only its SHA-256 hash) and never logged.
/// <see cref="IsNewUser"/> is true only on the login that created the account.
/// </summary>
public sealed record AuthTokensResponse(
    string AccessToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    Guid UserId,
    bool IsNewUser);
