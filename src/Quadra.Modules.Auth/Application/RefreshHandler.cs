using Microsoft.Extensions.Logging;
using Quadra.Modules.Auth.Cognito;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Persistence;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Orchestrates the FA.3 refresh flow: look up the local refresh-token row by hash → exchange the
/// raw token with Cognito (REFRESH_TOKEN_AUTH) → rotate if Cognito returned a new refresh token.
/// No <c>UserLoggedIn</c> event is published on refresh.
/// </summary>
public sealed class RefreshHandler
{
    private const string OutcomeRefreshRejected = "refresh_rejected";

    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly ICognitoAuthClient _cognitoAuthClient;
    private readonly IUserRepository _userRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RefreshHandler> _logger;

    public RefreshHandler(
        IRefreshTokenRepository refreshTokenRepository,
        ICognitoAuthClient cognitoAuthClient,
        IUserRepository userRepository,
        TimeProvider timeProvider,
        ILogger<RefreshHandler> logger)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _cognitoAuthClient = cognitoAuthClient;
        _userRepository = userRepository;
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

        var stored = await _refreshTokenRepository.FindByTokenHashAsync(tokenHash, cancellationToken)
            ;
        if (stored is null || !stored.IsActive(now))
        {
            _logger.LogInformation(
                "Refresh rejected: token unknown, revoked, or expired. {OutcomeCategory}",
                OutcomeRefreshRejected);
            throw new RefreshTokenRejectedException("The refresh token is unknown, revoked, or expired.");
        }

        CognitoAuthResult tokens;
        try
        {
            tokens = await _cognitoAuthClient
                .RefreshAsync(rawRefreshToken, stored.CognitoSub, cancellationToken)
                ;
        }
        catch (RefreshTokenRejectedException)
        {
            await _refreshTokenRepository.RevokeAsync(stored, now, cancellationToken);
            _logger.LogInformation(
                "Refresh rejected: Cognito rejected the token; local row revoked. {OutcomeCategory}",
                OutcomeRefreshRejected);
            throw;
        }

        var user = await _userRepository.FindByCognitoSubAsync(stored.CognitoSub, cancellationToken)
            ;
        if (user is null)
        {
            _logger.LogInformation(
                "Refresh rejected: no local users row for Cognito sub. {OutcomeCategory}",
                OutcomeRefreshRejected);
            throw new RefreshTokenRejectedException("No local user is linked to the refresh token.");
        }

        var returnedRefreshToken = rawRefreshToken;

        // Rotation: if Cognito issued a new refresh token, revoke the old row and persist a new one.
        if (!string.IsNullOrEmpty(tokens.RefreshToken)
            && !string.Equals(tokens.RefreshToken, rawRefreshToken, StringComparison.Ordinal))
        {
            await _refreshTokenRepository.RevokeAsync(stored, now, cancellationToken);

            var rotated = RefreshToken.Issue(
                Guid.NewGuid(),
                user.Id,
                RefreshTokenHasher.Hash(tokens.RefreshToken),
                user.CognitoSub,
                deviceId,
                now,
                tokens.RefreshTokenExpiresAt);

            await _refreshTokenRepository.AddAsync(rotated, cancellationToken);
            returnedRefreshToken = tokens.RefreshToken;
        }

        _logger.LogInformation("Refresh succeeded. {OutcomeCategory} {UserId}", "success", user.Id);

        return new AuthTokensResponse(
            tokens.AccessToken,
            tokens.IdToken,
            returnedRefreshToken,
            "Bearer",
            tokens.ExpiresIn,
            user.Id,
            user.CognitoSub,
            user.ConfirmationStatus.ToString());
    }
}
