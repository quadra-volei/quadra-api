using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Auth.Cognito;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Oidc;
using Quadra.Modules.Auth.Persistence;
using Quadra.Shared.Events.Auth;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Orchestrates the FA.3 Google/Apple login flow: validate the provider ID token → broker a
/// Cognito session for the already-linked user → resolve the local user → persist the refresh
/// token → publish <see cref="UserLoggedIn"/>.
/// </summary>
public sealed class OidcLoginHandler
{
    private readonly IOidcTokenValidator _oidcValidator;
    private readonly ICognitoAuthClient _cognitoAuthClient;
    private readonly IUserRepository _userRepository;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OidcLoginHandler> _logger;

    public OidcLoginHandler(
        IOidcTokenValidator oidcValidator,
        ICognitoAuthClient cognitoAuthClient,
        IUserRepository userRepository,
        IRefreshTokenRepository refreshTokenRepository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        ILogger<OidcLoginHandler> logger)
    {
        _oidcValidator = oidcValidator;
        _cognitoAuthClient = cognitoAuthClient;
        _userRepository = userRepository;
        _refreshTokenRepository = refreshTokenRepository;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AuthTokensResponse> HandleAsync(
        IdentityProvider provider,
        string idToken,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);

        var providerName = provider.ToString().ToLowerInvariant();
        var oidcProvider = provider switch
        {
            IdentityProvider.Google => OidcProvider.Google,
            IdentityProvider.Apple => OidcProvider.Apple,
            _ => throw new InvalidOperationException(
                $"OIDC login invoked with non-external provider {provider}."),
        };

        OidcClaims claims;
        try
        {
            claims = await _oidcValidator.ValidateAsync(idToken, oidcProvider, cancellationToken)
                ;
        }
        catch (OidcTokenInvalidException)
        {
            _logger.LogInformation(
                "Login rejected: OIDC token invalid. {Provider} {OutcomeCategory}",
                providerName,
                "oidc_invalid");
            throw;
        }

        CognitoAuthResult tokens;
        try
        {
            tokens = await _cognitoAuthClient
                .BrokerExternalSessionAsync(provider, claims.Subject, cancellationToken)
                ;
        }
        catch (UserNotFoundForLoginException)
        {
            _logger.LogInformation(
                "Login rejected: no linked user for external identity. {Provider} {OutcomeCategory}",
                providerName,
                "user_not_found");
            throw;
        }

        var user = await _userRepository.FindByCognitoSubAsync(tokens.CognitoSub, cancellationToken)
            ;
        if (user is null)
        {
            _logger.LogInformation(
                "Login rejected: no local users row for Cognito sub. {Provider} {OutcomeCategory}",
                providerName,
                "user_not_found");
            throw new UserNotFoundForLoginException("No local user is linked to the supplied external identity.");
        }

        var now = _timeProvider.GetUtcNow();
        var rawRefreshToken = tokens.RefreshToken
            ?? throw new CognitoUnavailableException("Cognito did not return a refresh token on external login.");

        var refreshToken = RefreshToken.Issue(
            Guid.NewGuid(),
            user.Id,
            RefreshTokenHasher.Hash(rawRefreshToken),
            user.CognitoSub,
            deviceId,
            now,
            tokens.RefreshTokenExpiresAt);

        await _refreshTokenRepository.AddAsync(refreshToken, cancellationToken);

        await PublishLoggedInAsync(user, providerName, deviceId, now, cancellationToken);

        _logger.LogInformation(
            "Login succeeded. {Provider} {OutcomeCategory} {UserId}",
            providerName,
            "success",
            user.Id);

        return new AuthTokensResponse(
            tokens.AccessToken,
            tokens.IdToken,
            rawRefreshToken,
            "Bearer",
            tokens.ExpiresIn,
            user.Id,
            user.CognitoSub,
            user.ConfirmationStatus.ToString());
    }

    private async Task PublishLoggedInAsync(
        User user,
        string providerName,
        string? deviceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var @event = new UserLoggedIn(user.Id, user.CognitoSub, providerName, deviceId, now);

        try
        {
            await _eventPublisher.PublishAsync(@event, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish UserLoggedIn for {UserId}. The session is committed; reconciliation required.",
                user.Id);
            // Do not rethrow — the session is already persisted.
        }
    }
}
