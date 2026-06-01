using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Auth.Cognito;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Persistence;
using Quadra.Shared.Events.Auth;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Orchestrates the FA.3 SMS OTP login flow: <c>initiate</c> (Cognito code delivery) and
/// <c>verify</c> (challenge response → confirmation flip → refresh-token persist → publish
/// <see cref="UserLoggedIn"/>).
/// </summary>
public sealed class SmsOtpLoginHandler
{
    public const string StepInitiate = "initiate";
    public const string StepVerify = "verify";

    private const string Provider = "phone";

    private readonly AuthDbContext _dbContext;
    private readonly ICognitoAuthClient _cognitoAuthClient;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SmsOtpLoginHandler> _logger;

    public SmsOtpLoginHandler(
        AuthDbContext dbContext,
        ICognitoAuthClient cognitoAuthClient,
        IRefreshTokenRepository refreshTokenRepository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        ILogger<SmsOtpLoginHandler> logger)
    {
        _dbContext = dbContext;
        _cognitoAuthClient = cognitoAuthClient;
        _refreshTokenRepository = refreshTokenRepository;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<SmsOtpInitiateResponse> InitiateAsync(
        string phoneNumber,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            _logger.LogInformation(
                "Login rejected: no local user for phone. {Provider} {Step} {OutcomeCategory}",
                Provider,
                StepInitiate,
                "user_not_found");
            throw new UserNotFoundForLoginException("No user is registered for the supplied phone number.");
        }

        var challenge = await _cognitoAuthClient.InitiateSmsOtpAsync(phoneNumber, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Login OTP initiated. {Provider} {Step} {OutcomeCategory}",
            Provider,
            StepInitiate,
            "success");

        return new SmsOtpInitiateResponse(
            challenge.Session,
            new SmsDeliveryDetails(challenge.DeliveryMedium, challenge.DeliveryDestination));
    }

    public async Task<AuthTokensResponse> VerifyAsync(
        string phoneNumber,
        string session,
        string code,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber, cancellationToken)
            .ConfigureAwait(false);
        if (user is null)
        {
            _logger.LogInformation(
                "Login rejected: no local user for phone. {Provider} {Step} {OutcomeCategory}",
                Provider,
                StepVerify,
                "user_not_found");
            throw new UserNotFoundForLoginException("No user is registered for the supplied phone number.");
        }

        CognitoAuthResult tokens;
        try
        {
            tokens = await _cognitoAuthClient
                .RespondToSmsOtpAsync(phoneNumber, session, code, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (InvalidOtpException)
        {
            _logger.LogInformation(
                "Login rejected: invalid OTP. {Provider} {Step} {OutcomeCategory}",
                Provider,
                StepVerify,
                "invalid_otp");
            throw;
        }
        catch (OtpChallengeExpiredException)
        {
            _logger.LogInformation(
                "Login rejected: OTP expired. {Provider} {Step} {OutcomeCategory}",
                Provider,
                StepVerify,
                "otp_expired");
            throw;
        }

        var now = _timeProvider.GetUtcNow();

        // First successful OTP confirms the phone account (completes the FA.2 phone signup).
        user.Confirm(now);

        var refreshToken = PersistRefreshToken(user, tokens, deviceId, now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await PublishLoggedInAsync(user, deviceId, now, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Login succeeded. {Provider} {Step} {OutcomeCategory} {UserId}",
            Provider,
            StepVerify,
            "success",
            user.Id);

        return BuildResponse(user, tokens, refreshToken);
    }

    private RefreshToken PersistRefreshToken(
        User user,
        CognitoAuthResult tokens,
        string? deviceId,
        DateTimeOffset now)
    {
        // The verify leg always yields a fresh refresh token from Cognito.
        var rawRefreshToken = tokens.RefreshToken
            ?? throw new CognitoUnavailableException("Cognito did not return a refresh token on OTP verify.");

        var entity = RefreshToken.Issue(
            Guid.NewGuid(),
            user.Id,
            RefreshTokenHasher.Hash(rawRefreshToken),
            user.CognitoSub,
            deviceId,
            now,
            tokens.RefreshTokenExpiresAt);

        _dbContext.RefreshTokens.Add(entity);
        return entity;
    }

    private async Task PublishLoggedInAsync(
        User user,
        string? deviceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var @event = new UserLoggedIn(user.Id, user.CognitoSub, Provider, deviceId, now);

        try
        {
            await _eventPublisher.PublishAsync(@event, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish UserLoggedIn for {UserId}. The session is committed; reconciliation required.",
                user.Id);
            // Do not rethrow — the session is already persisted; an outbox publisher lands later.
        }
    }

    private static AuthTokensResponse BuildResponse(User user, CognitoAuthResult tokens, RefreshToken persisted)
    {
        _ = persisted;
        return new AuthTokensResponse(
            tokens.AccessToken,
            tokens.IdToken,
            tokens.RefreshToken!,
            "Bearer",
            tokens.ExpiresIn,
            user.Id,
            user.CognitoSub,
            user.ConfirmationStatus.ToString());
    }
}
