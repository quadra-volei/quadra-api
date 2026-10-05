using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Auth.PhoneVerification;
using Quadra.Modules.Auth.Tokens;
using Quadra.Shared.Events.Auth;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Orchestrates the FA.3 SMS OTP login flow: <c>initiate</c> (code delivery, for any phone number)
/// and <c>verify</c> (code check → find or create the user → issue a session → publish
/// <see cref="UserLoggedIn"/>). The first valid OTP for a phone number creates its account.
/// </summary>
public sealed class SmsOtpLoginHandler
{
    public const string StepInitiate = "initiate";
    public const string StepVerify = "verify";

    private const string Provider = "phone";
    private const string DeliveryMedium = "SMS";

    private readonly IPhoneVerificationService _phoneVerification;
    private readonly UserProvisioner _userProvisioner;
    private readonly SessionFactory _sessionFactory;
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<SmsOtpLoginHandler> _logger;

    public SmsOtpLoginHandler(
        IPhoneVerificationService phoneVerification,
        UserProvisioner userProvisioner,
        SessionFactory sessionFactory,
        IRefreshTokenRepository refreshTokenRepository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        ILogger<SmsOtpLoginHandler> logger)
    {
        _phoneVerification = phoneVerification;
        _userProvisioner = userProvisioner;
        _sessionFactory = sessionFactory;
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

        // No user lookup: the response is identical for known and unknown numbers, so the
        // endpoint cannot be used to probe which phones have an account.
        await _phoneVerification.StartAsync(phoneNumber, cancellationToken);

        _logger.LogInformation(
            "Login OTP initiated. {Provider} {Step} {OutcomeCategory}",
            Provider,
            StepInitiate,
            "success");

        return new SmsOtpInitiateResponse(
            new SmsDeliveryDetails(DeliveryMedium, MaskPhoneNumber(phoneNumber)));
    }

    public async Task<AuthTokensResponse> VerifyAsync(
        string phoneNumber,
        string code,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var result = await _phoneVerification.CheckAsync(phoneNumber, code, cancellationToken);
        switch (result)
        {
            case PhoneVerificationResult.Approved:
                break;
            case PhoneVerificationResult.InvalidCode:
                _logger.LogInformation(
                    "Login rejected: invalid OTP. {Provider} {Step} {OutcomeCategory}",
                    Provider,
                    StepVerify,
                    "invalid_otp");
                throw new InvalidOtpException("The OTP code is incorrect.");
            case PhoneVerificationResult.Expired:
                _logger.LogInformation(
                    "Login rejected: OTP expired. {Provider} {Step} {OutcomeCategory}",
                    Provider,
                    StepVerify,
                    "otp_expired");
                throw new OtpChallengeExpiredException("The OTP code has expired. Request a new one.");
            default:
                throw new InvalidOperationException($"Unknown phone verification result {result}.");
        }

        var now = _timeProvider.GetUtcNow();

        var provisioned = await _userProvisioner
            .GetOrCreateForPhoneAsync(phoneNumber, now, cancellationToken);
        var user = provisioned.User;

        var session = _sessionFactory.Create(user, deviceId, provisioned.IsNew, now);
        await _refreshTokenRepository.AddAsync(session.RefreshTokenRow, cancellationToken);

        await PublishLoggedInAsync(user, deviceId, now, cancellationToken);

        _logger.LogInformation(
            "Login succeeded. {Provider} {Step} {OutcomeCategory} {UserId} {IsNewUser}",
            Provider,
            StepVerify,
            "success",
            user.Id,
            provisioned.IsNew);

        return session.Tokens;
    }

    private async Task PublishLoggedInAsync(
        User user,
        string? deviceId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var @event = new UserLoggedIn(user.Id, Provider, deviceId, now);

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
            // Do not rethrow — the session is already persisted; an outbox publisher lands later.
        }
    }

    private static string MaskPhoneNumber(string phoneNumber)
    {
        if (phoneNumber.Length <= 5)
        {
            return new string('*', phoneNumber.Length);
        }

        var prefix = phoneNumber[..3];
        var suffix = phoneNumber[^2..];
        var masked = new string('*', phoneNumber.Length - prefix.Length - suffix.Length);
        return $"{prefix}{masked}{suffix}";
    }
}
