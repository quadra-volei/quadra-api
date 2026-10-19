using Microsoft.Extensions.Logging;
using Quadra.Modules.Auth.Persistence;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Ends one session by revoking its refresh-token family. Idempotent and silent: an unknown or already
/// revoked token is not an error, so the endpoint reveals nothing about token validity. The
/// session's access tokens stop working at once (their <c>sid</c> is checked on every request).
/// </summary>
public sealed class LogoutHandler
{
    private readonly IRefreshTokenRepository _refreshTokenRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<LogoutHandler> _logger;

    public LogoutHandler(
        IRefreshTokenRepository refreshTokenRepository,
        TimeProvider timeProvider,
        ILogger<LogoutHandler> logger)
    {
        _refreshTokenRepository = refreshTokenRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(string rawRefreshToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawRefreshToken);

        var now = _timeProvider.GetUtcNow();
        var stored = await _refreshTokenRepository
            .FindByTokenHashAsync(RefreshTokenHasher.Hash(rawRefreshToken), cancellationToken);
        if (stored is null || !stored.IsActive(now))
        {
            return;
        }

        // The whole session: its access tokens stop working too (they carry the family as sid).
        await _refreshTokenRepository.RevokeFamilyAsync(stored.FamilyId, now, cancellationToken);

        _logger.LogInformation("Logout succeeded. {OutcomeCategory} {UserId}", "success", stored.UserId);
    }
}
