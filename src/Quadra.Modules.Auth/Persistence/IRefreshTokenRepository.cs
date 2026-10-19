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
    /// Atomically revokes <paramref name="current"/> and inserts <paramref name="replacement"/>.
    /// Returns <c>false</c> — inserting nothing — when <paramref name="current"/> was already
    /// revoked, so two concurrent refreshes of the same token cannot both succeed.
    /// </summary>
    Task<bool> TryRotateAsync(
        RefreshToken current,
        RefreshToken replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>Revokes every still-active token of a session (logout, reuse of a rotated token).</summary>
    Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// True while the session has an unrevoked, unexpired token of that user. Deleting the account
    /// deletes its tokens (cascade), so this also refuses tokens of deleted accounts (SEC-10).
    /// </summary>
    Task<bool> IsSessionActiveAsync(Guid userId, Guid familyId, DateTimeOffset now, CancellationToken cancellationToken);
}
