using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Oidc;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Auth.Tokens;
using Quadra.Shared.Events.Auth;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Orchestrates the FA.3 Google/Apple login flow: validate the provider ID token → find or create
/// the local user for that identity → issue a session → publish <see cref="UserLoggedIn"/>.
/// </summary>
public sealed class OidcLoginHandler
{
    private readonly IOidcTokenValidator _oidcValidator;
    private readonly UserProvisioner _userProvisioner;
    private readonly SessionFactory _sessionFactory;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OidcLoginHandler> _logger;

    public OidcLoginHandler(
        IOidcTokenValidator oidcValidator,
        UserProvisioner userProvisioner,
        SessionFactory sessionFactory,
        IRefreshTokenRepository refreshTokenRepository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        ILogger<OidcLoginHandler> logger)
    {
        _oidcValidator = oidcValidator;
        _userProvisioner = userProvisioner;
        _sessionFactory = sessionFactory;
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
            claims = await _oidcValidator.ValidateAsync(idToken, oidcProvider, cancellationToken);
        }
        catch (OidcTokenInvalidException)
        {
            _logger.LogInformation(
                "Login rejected: OIDC token invalid. {Provider} {OutcomeCategory}",
                providerName,
                "oidc_invalid");
            throw;
        }

        var now = _timeProvider.GetUtcNow();

        var provisioned = await _userProvisioner.GetOrCreateForExternalAsync(
            provider,
            claims.Subject,
            claims.Email,
            now,
            cancellationToken);
        var user = provisioned.User;

        var session = _sessionFactory.Create(user, deviceId, provisioned.IsNew, now);
        await _refreshTokenRepository.AddAsync(session.RefreshTokenRow, cancellationToken);

        await PublishLoggedInAsync(user, providerName, deviceId, now, cancellationToken);

        _logger.LogInformation(
            "Login succeeded. {Provider} {OutcomeCategory} {UserId} {IsNewUser}",
            providerName,
            "success",
            user.Id,
            provisioned.IsNew);

        return session.Tokens;
    }

    private async Task PublishLoggedInAsync(
        User user,
        string providerName,
        string? deviceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var @event = new UserLoggedIn(user.Id, providerName, deviceId, now);

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
