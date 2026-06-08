using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Auth.Cognito;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Oidc;
using Quadra.Modules.Auth.Persistence;
using Quadra.Shared.Events.Auth;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Orchestrates the FA.2 signup pipeline:
/// dispatch → Cognito → DB insert (transactional) → publish <see cref="UserRegistered"/>.
/// </summary>
public sealed class SignupHandler
{
    private const string PostgresUniqueViolation = "23505";

    private readonly IUserRepository _userRepository;
    private readonly AuthDbContext _dbContext;
    private readonly ICognitoSignupClient _cognitoClient;
    private readonly IOidcTokenValidator _oidcValidator;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SignupHandler> _logger;

    public SignupHandler(
        IUserRepository userRepository,
        AuthDbContext dbContext,
        ICognitoSignupClient cognitoClient,
        IOidcTokenValidator oidcValidator,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        ILogger<SignupHandler> logger)
    {
        _userRepository = userRepository;
        _dbContext = dbContext;
        _cognitoClient = cognitoClient;
        _oidcValidator = oidcValidator;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SignupResponse> HandleAsync(SignupCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command switch
        {
            SignupCommand.Phone phone => await HandlePhoneAsync(phone, cancellationToken),
            SignupCommand.External external => await HandleExternalAsync(external, cancellationToken),
            _ => throw new InvalidOperationException($"Unsupported signup command type: {command.GetType().Name}"),
        };
    }

    private async Task<SignupResponse> HandlePhoneAsync(
        SignupCommand.Phone command,
        CancellationToken cancellationToken)
    {
        var existing = await _userRepository.FindByPhoneNumberAsync(command.PhoneNumber, cancellationToken);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Signup rejected: phone already registered. {Provider} {OutcomeCategory}",
                "phone",
                "duplicate_phone");
            throw new PhoneAlreadyRegisteredException(command.PhoneNumber);
        }

        var cognitoResult = await _cognitoClient.SignUpPhoneAsync(command.PhoneNumber, cancellationToken);

        var now = _timeProvider.GetUtcNow();
        var user = User.CreateForPhone(Guid.NewGuid(), cognitoResult.UserSub, command.PhoneNumber, now);

        await PersistUserAsync(user, cancellationToken);

        await PublishUserRegisteredAsync(user, now, cancellationToken);

        _logger.LogInformation(
            "Signup succeeded. {Provider} {OutcomeCategory} {UserId}",
            "phone",
            "success",
            user.Id);

        return new SignupResponse(
            user.Id,
            user.CognitoSub,
            "phone",
            user.ConfirmationStatus.ToString(),
            new SmsDeliveryDetails(cognitoResult.DeliveryMedium, cognitoResult.DeliveryDestination));
    }

    private async Task<SignupResponse> HandleExternalAsync(
        SignupCommand.External command,
        CancellationToken cancellationToken)
    {
        var providerName = command.ExternalProvider.ToString().ToLowerInvariant();

        var oidcProvider = command.ExternalProvider switch
        {
            IdentityProvider.Google => OidcProvider.Google,
            IdentityProvider.Apple => OidcProvider.Apple,
            _ => throw new InvalidOperationException(
                $"External signup invoked with non-external provider {command.ExternalProvider}."),
        };

        OidcClaims claims;
        try
        {
            claims = await _oidcValidator.ValidateAsync(command.IdToken, oidcProvider, cancellationToken);
        }
        catch (OidcTokenInvalidException)
        {
            _logger.LogInformation(
                "Signup rejected: OIDC token invalid. {Provider} {OutcomeCategory}",
                providerName,
                "oidc_invalid");
            throw;
        }

        var existingSub = await _cognitoClient.FindExternalUserSubAsync(
            command.ExternalProvider,
            claims.Subject,
            cancellationToken);
        if (existingSub is not null)
        {
            _logger.LogInformation(
                "Signup rejected: external identity already linked. {Provider} {OutcomeCategory}",
                providerName,
                "duplicate_external");
            throw new ExternalIdentityAlreadyLinkedException(providerName, claims.Subject);
        }

        var normalizedEmail = string.IsNullOrWhiteSpace(claims.Email)
            ? null
            : claims.Email.Trim().ToLowerInvariant();

        var cognitoResult = await _cognitoClient.ProvisionExternalUserAsync(
            command.ExternalProvider,
            claims.Subject,
            normalizedEmail,
            claims.EmailVerified,
            cancellationToken);

        try
        {
            await _cognitoClient.LinkExternalIdentityAsync(
                command.ExternalProvider,
                cognitoResult.UserSub,
                claims.Subject,
                cancellationToken);
        }
        catch (ExternalIdentityAlreadyLinkedException)
        {
            _logger.LogInformation(
                "Signup rejected at link step: external identity already linked. {Provider} {OutcomeCategory}",
                providerName,
                "duplicate_external");
            throw;
        }

        var now = _timeProvider.GetUtcNow();
        var user = User.CreateForExternal(
            Guid.NewGuid(),
            cognitoResult.UserSub,
            command.ExternalProvider,
            normalizedEmail,
            now);

        await PersistUserAsync(user, cancellationToken);

        await PublishUserRegisteredAsync(user, now, cancellationToken);

        _logger.LogInformation(
            "Signup succeeded. {Provider} {OutcomeCategory} {UserId}",
            providerName,
            "success",
            user.Id);

        return new SignupResponse(
            user.Id,
            user.CognitoSub,
            providerName,
            user.ConfirmationStatus.ToString(),
            SmsDelivery: null);
    }

    private async Task PersistUserAsync(User user, CancellationToken cancellationToken)
    {
        try
        {
            await _userRepository.AddAsync(user, cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogWarning(ex, "Race detected inserting user {CognitoSub}", user.CognitoSub);
            if (user.Provider == IdentityProvider.Phone && user.PhoneNumber is not null)
            {
                throw new PhoneAlreadyRegisteredException(user.PhoneNumber);
            }

            throw new ExternalIdentityAlreadyLinkedException(
                user.Provider.ToString().ToLowerInvariant(),
                user.CognitoSub);
        }
    }

    private async Task PublishUserRegisteredAsync(
        User user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var @event = new UserRegistered(
            user.Id,
            user.CognitoSub,
            user.Provider.ToString().ToLowerInvariant(),
            user.PhoneNumber,
            user.Email,
            user.ConfirmationStatus.ToString(),
            now);

        try
        {
            await _eventPublisher.PublishAsync(@event, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish UserRegistered for {UserId}. The DB row is committed; reconciliation required.",
                user.Id);
            // Do not rethrow — the user is already persisted; surfacing this as 5xx would
            // confuse the client. A future spec will add an outbox-based publisher.
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        return ex.InnerException is PostgresException pg
            && string.Equals(pg.SqlState, PostgresUniqueViolation, StringComparison.Ordinal);
    }
}
