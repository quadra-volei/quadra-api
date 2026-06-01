using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Cognito;

/// <summary>
/// Internal abstraction over the Cognito Identity Provider SDK used by the FA.3 login flows.
/// Keeps the SDK contained to the Auth module per the module-boundary rule and translates SDK
/// exceptions into the application-layer login exceptions.
/// </summary>
public interface ICognitoAuthClient
{
    /// <summary>
    /// Starts an SMS OTP auth challenge for the user identified by <paramref name="username"/>
    /// (the Cognito username, i.e. the E.164 phone for phone signups). Cognito dispatches the SMS
    /// and returns the challenge session + masked delivery details.
    /// </summary>
    Task<SmsOtpChallenge> InitiateSmsOtpAsync(string username, CancellationToken cancellationToken);

    /// <summary>
    /// Answers an SMS OTP challenge with the code the user received, returning Cognito tokens on
    /// success. <paramref name="username"/> is the Cognito username tied to the challenge.
    /// </summary>
    Task<CognitoAuthResult> RespondToSmsOtpAsync(
        string username,
        string session,
        string code,
        CancellationToken cancellationToken);

    /// <summary>
    /// Mints a Cognito session for a user whose external (Google/Apple) identity was already
    /// validated and linked during FA.2 signup, without using the Hosted UI.
    /// </summary>
    Task<CognitoAuthResult> BrokerExternalSessionAsync(
        IdentityProvider provider,
        string externalSub,
        CancellationToken cancellationToken);

    /// <summary>
    /// Exchanges a raw Cognito refresh token (REFRESH_TOKEN_AUTH) for a fresh access + ID token.
    /// <paramref name="cognitoSub"/> is the username component used for the SecretHash.
    /// </summary>
    Task<CognitoAuthResult> RefreshAsync(
        string refreshToken,
        string cognitoSub,
        CancellationToken cancellationToken);
}
