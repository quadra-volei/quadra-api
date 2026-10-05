using Microsoft.Extensions.Logging;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Persistence;
using Quadra.Modules.Auth.Tokens;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Orchestrates the FA.3 refresh flow: look up the refresh-token row by hash → issue a new access
/// token and rotate the refresh token (the presented one is revoked and can never be used again).
/// No <c>UserLoggedIn</c> event is published on refresh.
/// </summary>
public sealed class RefreshHandler
{
    private const string OutcomeRefreshRejected = "refresh_rejected";

    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly IUserRepository _userRepository;
    private readonly SessionFactory _sessionFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RefreshHandler> _logger;

    public RefreshHandler(
        IRefreshTokenRepository refreshTokenRepository,
        IUserRepository userRepository,
        SessionFactory sessionFactory,
        TimeProvider timeProvider,
        ILogger<RefreshHandler> logger)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _userRepository = userRepository;
        _sessionFactory = sessionFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<AuthTokensResponse> HandleAsync(
        string rawRefreshToken,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var now = _timeProvider.GetUtcNow();
        var tokenHash = RefreshTokenHasher.Hash(rawRefreshToken);

        var stored = await _refreshTokenRepository.FindByTokenHashAsync(tokenHash, cancellationToken);
        if (stored is null || !stored.IsActive(now))
        {
            _logger.LogInformation(
                "Refresh rejected: token unknown, revoked, or expired. {OutcomeCategory}",
                OutcomeRefreshRejected);
            throw new RefreshTokenRejectedException("The refresh token is unknown, revoked, or expired.");
        }

        var user = await _userRepository.FindByIdAsync(stored.UserId, cancellationToken);
        if (user is null)
        {
            _logger.LogInformation(
                "Refresh rejected: no users row for the token. {OutcomeCategory}",
                OutcomeRefreshRejected);
            throw new RefreshTokenRejectedException("No user is linked to the refresh token.");
        }

        var session = _sessionFactory.Create(user, deviceId ?? stored.DeviceId, isNewUser: false, now);

        var rotated = await _refreshTokenRepository
            .TryRotateAsync(stored, session.RefreshTokenRow, now, cancellationToken);
        if (!rotated)
        {
            _logger.LogInformation(
                "Refresh rejected: token was rotated by a concurrent request. {OutcomeCategory}",
                OutcomeRefreshRejected);
            throw new RefreshTokenRejectedException("The refresh token was already used.");
        }

        _logger.LogInformation("Refresh succeeded. {OutcomeCategory} {UserId}", "success", user.Id);

        return session.Tokens;
    }
}
