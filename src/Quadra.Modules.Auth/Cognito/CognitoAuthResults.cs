namespace Quadra.Modules.Auth.Cognito;

/// <summary>
/// Outcome of an SMS OTP "initiate" call. <see cref="Session"/> is the opaque Cognito challenge
/// session the client echoes back on "verify". <see cref="DeliveryMedium"/> /
/// <see cref="DeliveryDestination"/> describe the dispatched SMS (masked destination).
/// </summary>
public sealed record SmsOtpChallenge(
    string Session,
    string DeliveryMedium,
    string DeliveryDestination);

/// <summary>
/// Tokens minted by a successful Cognito auth (OTP verify, external broker, or refresh).
/// <see cref="RefreshTokenExpiresAt"/> is the absolute expiry derived from the refresh-token
/// validity; <see cref="RefreshToken"/> is null when Cognito did not return a new one (rotation
/// disabled on a REFRESH_TOKEN_AUTH call).
/// </summary>
public sealed record CognitoAuthResult(
    string AccessToken,
    string IdToken,
    string? RefreshToken,
    int ExpiresIn,
    DateTimeOffset RefreshTokenExpiresAt,
    string CognitoSub);
