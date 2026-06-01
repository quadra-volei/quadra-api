using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Persistence;

/// <summary>
/// Persistence operations on the <c>refresh_tokens</c> table. Scoped lifetime.
/// </summary>
public interface IRefreshTokenRepository
{
    /// <summary>
    /// Returns the tracked <see cref="RefreshToken"/> row matching the SHA-256 hex hash, or
    /// <c>null</c> if none exists. Tracked so the caller can revoke it in the same unit of work.
    /// </summary>
    Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts a new refresh-token row and commits.
    /// </summary>
    Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken);

    /// <summary>
    /// Marks the supplied tracked row revoked and commits.
    /// </summary>
    Task RevokeAsync(RefreshToken refreshToken, DateTimeOffset now, CancellationToken cancellationToken);
}
