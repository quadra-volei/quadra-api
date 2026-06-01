namespace Quadra.Modules.Auth.Contracts;

/// <summary>
/// Shared 200 OK body for every token-issuing login leg (SMS OTP verify, Google, Apple, refresh).
/// The raw <see cref="RefreshToken"/> is returned to the client exactly once per issuance and is
/// never persisted (only its SHA-256 hash) and never logged.
/// </summary>
public sealed record AuthTokensResponse(
    string AccessToken,
    string IdToken,
    string RefreshToken,
    string TokenType,
    int ExpiresIn,
    Guid UserId,
    string CognitoSub,
    string ConfirmationStatus);
