using Amazon.CognitoIdentityProvider;
using Amazon.CognitoIdentityProvider.Model;
using Microsoft.Extensions.Options;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Cognito;

/// <summary>
/// <see cref="ICognitoAuthClient"/> implementation that wraps <see cref="IAmazonCognitoIdentityProvider"/>.
/// Translates AWS SDK exceptions into the application-layer login exceptions so the handlers and
/// controller never reference the SDK directly. Reuses <see cref="SecretHashCalculator"/>.
/// </summary>
public sealed class CognitoAuthClient : ICognitoAuthClient
{
    private const string GoogleProviderName = "Google";
    private const string AppleProviderName = "SignInWithApple";

    // Default Cognito refresh-token validity is 30 days. The exact value lives on the App Client
    // config (not exposed via the SDK auth responses), so we derive a conservative absolute expiry
    // from this constant; a cleanup job (out of scope) uses the expires_at index.
    private static readonly TimeSpan RefreshTokenValidity = TimeSpan.FromDays(30);

    private readonly IAmazonCognitoIdentityProvider _cognito;
    private readonly CognitoSignupOptions _options;
    private readonly TimeProvider _timeProvider;

    public CognitoAuthClient(
        IAmazonCognitoIdentityProvider cognito,
        IOptions<CognitoSignupOptions> options,
        TimeProvider timeProvider)
    {
        _cognito = cognito;
        _options = options.Value;
        _timeProvider = timeProvider;
    }

    public async Task<SmsOtpChallenge> InitiateSmsOtpAsync(
        string username,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);

        var request = new AdminInitiateAuthRequest
        {
            UserPoolId = _options.UserPoolId,
            ClientId = _options.AppClientId,
            AuthFlow = AuthFlowType.CUSTOM_AUTH,
            AuthParameters = BuildAuthParameters(username),
        };

        try
        {
            var response = await _cognito.AdminInitiateAuthAsync(request, cancellationToken)
                .ConfigureAwait(false);

            var medium = ReadChallengeParameter(response.ChallengeParameters, "CODE_DELIVERY_DELIVERY_MEDIUM")
                ?? "SMS";
            var destination = ReadChallengeParameter(response.ChallengeParameters, "CODE_DELIVERY_DESTINATION")
                ?? MaskUsername(username);

            return new SmsOtpChallenge(response.Session, medium, destination);
        }
        catch (UserNotFoundException ex)
        {
            throw new UserNotFoundForLoginException(ex.Message);
        }
        catch (Exception ex) when (IsThrottle(ex))
        {
            throw new CognitoAuthThrottledException("Cognito throttled the OTP request.", ex);
        }
        catch (AmazonCognitoIdentityProviderException ex)
        {
            throw new CognitoUnavailableException("Cognito AdminInitiateAuth (SMS OTP) failed.", ex);
        }
    }

    public async Task<CognitoAuthResult> RespondToSmsOtpAsync(
        string username,
        string session,
        string code,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var challengeResponses = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["USERNAME"] = username,
            ["ANSWER"] = code,
        };

        ApplySecretHash(challengeResponses, username);

        var request = new AdminRespondToAuthChallengeRequest
        {
            UserPoolId = _options.UserPoolId,
            ClientId = _options.AppClientId,
            ChallengeName = ChallengeNameType.CUSTOM_CHALLENGE,
            Session = session,
            ChallengeResponses = challengeResponses,
        };

        try
        {
            var response = await _cognito.AdminRespondToAuthChallengeAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.AuthenticationResult is null)
            {
                // Cognito kept the challenge open instead of authenticating — the code was wrong.
                throw new InvalidOtpException("The OTP code was rejected by Cognito.");
            }

            return ToAuthResult(response.AuthenticationResult);
        }
        catch (CodeMismatchException ex)
        {
            throw new InvalidOtpException("The OTP code is incorrect.", ex);
        }
        catch (ExpiredCodeException ex)
        {
            throw new OtpChallengeExpiredException("The OTP code has expired.", ex);
        }
        catch (NotAuthorizedException ex)
        {
            throw new InvalidOtpException("The OTP challenge was rejected.", ex);
        }
        catch (Exception ex) when (IsThrottle(ex))
        {
            throw new CognitoAuthThrottledException("Cognito throttled the OTP verification.", ex);
        }
        catch (AmazonCognitoIdentityProviderException ex)
        {
            throw new CognitoUnavailableException("Cognito AdminRespondToAuthChallenge failed.", ex);
        }
    }

    public async Task<CognitoAuthResult> BrokerExternalSessionAsync(
        IdentityProvider provider,
        string externalSub,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalSub);

        // ASSUMPTION (flagged "Decision needed" in FA.3 spec): FA.2 links the validated Google/Apple
        // identity to a Cognito-native destination user via AdminLinkProviderForUser. To mint a
        // session without the Hosted UI we resolve that linked Cognito username and run an
        // admin-initiated CUSTOM_AUTH flow against it (consistent with FA.2's server-side,
        // no-redirect stance and with the SMS OTP path above, which also uses CUSTOM_AUTH).
        // The pool must be configured for admin-initiated custom auth; this mirrors the SMS path.
        var username = await ResolveLinkedUsernameAsync(provider, externalSub, cancellationToken)
            .ConfigureAwait(false);
        if (username is null)
        {
            throw new UserNotFoundForLoginException(
                "No Cognito user is linked to the supplied external identity.");
        }

        var request = new AdminInitiateAuthRequest
        {
            UserPoolId = _options.UserPoolId,
            ClientId = _options.AppClientId,
            AuthFlow = AuthFlowType.CUSTOM_AUTH,
            AuthParameters = BuildAuthParameters(username),
        };

        try
        {
            var response = await _cognito.AdminInitiateAuthAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.AuthenticationResult is null)
            {
                // The custom-auth Lambda is expected to authenticate a server-validated external
                // identity in a single step. A pending challenge here means the pool is not
                // configured for that — surface it as a service error rather than guessing.
                throw new CognitoUnavailableException(
                    "Cognito did not return tokens for the linked external user; the pool is not "
                    + "configured for admin-initiated custom auth brokering.");
            }

            return ToAuthResult(response.AuthenticationResult);
        }
        catch (UserNotFoundException ex)
        {
            throw new UserNotFoundForLoginException(ex.Message);
        }
        catch (Exception ex) when (IsThrottle(ex))
        {
            throw new CognitoAuthThrottledException("Cognito throttled the external login.", ex);
        }
        catch (AmazonCognitoIdentityProviderException ex)
        {
            throw new CognitoUnavailableException("Cognito AdminInitiateAuth (external) failed.", ex);
        }
    }

    public async Task<CognitoAuthResult> RefreshAsync(
        string refreshToken,
        string cognitoSub,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(cognitoSub);

        var authParameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["REFRESH_TOKEN"] = refreshToken,
        };

        // For REFRESH_TOKEN_AUTH the SecretHash username component is the Cognito sub/username,
        // not the refresh token (per FA.3 implementation notes).
        ApplySecretHash(authParameters, cognitoSub);

        var request = new AdminInitiateAuthRequest
        {
            UserPoolId = _options.UserPoolId,
            ClientId = _options.AppClientId,
            AuthFlow = AuthFlowType.REFRESH_TOKEN_AUTH,
            AuthParameters = authParameters,
        };

        try
        {
            var response = await _cognito.AdminInitiateAuthAsync(request, cancellationToken)
                .ConfigureAwait(false);

            if (response.AuthenticationResult is null)
            {
                throw new RefreshTokenRejectedException("Cognito did not return tokens for the refresh.");
            }

            // Cognito does not echo the refresh token on REFRESH_TOKEN_AUTH unless rotation is on.
            return ToAuthResult(response.AuthenticationResult, fallbackSub: cognitoSub);
        }
        catch (NotAuthorizedException ex)
        {
            throw new RefreshTokenRejectedException("The refresh token was rejected by Cognito.", ex);
        }
        catch (Exception ex) when (IsThrottle(ex))
        {
            throw new CognitoAuthThrottledException("Cognito throttled the refresh.", ex);
        }
        catch (AmazonCognitoIdentityProviderException ex)
        {
            throw new CognitoUnavailableException("Cognito AdminInitiateAuth (refresh) failed.", ex);
        }
    }

    private async Task<string?> ResolveLinkedUsernameAsync(
        IdentityProvider provider,
        string externalSub,
        CancellationToken cancellationToken)
    {
        var providerName = MapProviderName(provider);
        var filter = $"identities.providerName = \"{providerName}\" and identities.userId = \"{externalSub}\"";

        var request = new ListUsersRequest
        {
            UserPoolId = _options.UserPoolId,
            Filter = filter,
            Limit = 1,
        };

        try
        {
            var response = await _cognito.ListUsersAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return response.Users?.FirstOrDefault()?.Username;
        }
        catch (AmazonCognitoIdentityProviderException ex)
        {
            throw new CognitoUnavailableException("Cognito ListUsers (external broker) failed.", ex);
        }
    }

    private Dictionary<string, string> BuildAuthParameters(string username)
    {
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["USERNAME"] = username,
        };

        ApplySecretHash(parameters, username);
        return parameters;
    }

    private void ApplySecretHash(IDictionary<string, string> parameters, string username)
    {
        if (!string.IsNullOrEmpty(_options.AppClientSecret))
        {
            parameters["SECRET_HASH"] = SecretHashCalculator.Compute(
                username,
                _options.AppClientId,
                _options.AppClientSecret);
        }
    }

    private CognitoAuthResult ToAuthResult(AuthenticationResultType result, string? fallbackSub = null)
    {
        var sub = ReadSubFromAccessToken(result.AccessToken) ?? fallbackSub ?? string.Empty;
        var expiresAt = _timeProvider.GetUtcNow().Add(RefreshTokenValidity);

        return new CognitoAuthResult(
            result.AccessToken,
            result.IdToken,
            result.RefreshToken,
            result.ExpiresIn ?? 0,
            expiresAt,
            sub);
    }

    private static string? ReadSubFromAccessToken(string accessToken)
    {
        // Cognito access tokens carry the `sub` claim. Read it without validating the signature
        // (FA.1 owns validation); this is only to correlate the token with the local users row.
        // Uses the same Microsoft.IdentityModel JSON web token reader as the OIDC validator.
        try
        {
            var jwt = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(accessToken);
            return jwt.Subject;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string? ReadChallengeParameter(
        IDictionary<string, string>? parameters,
        string key)
    {
        if (parameters is not null && parameters.TryGetValue(key, out var value))
        {
            return value;
        }

        return null;
    }

    private static bool IsThrottle(Exception ex) =>
        ex is TooManyRequestsException or LimitExceededException;

    private static string MapProviderName(IdentityProvider provider) => provider switch
    {
        IdentityProvider.Google => GoogleProviderName,
        IdentityProvider.Apple => AppleProviderName,
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
    };

    private static string MaskUsername(string username)
    {
        if (username.Length <= 5)
        {
            return new string('*', username.Length);
        }

        var prefix = username[..3];
        var suffix = username[^2..];
        var masked = new string('*', username.Length - prefix.Length - suffix.Length);
        return $"{prefix}{masked}{suffix}";
    }
}
