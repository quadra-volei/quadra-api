using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IRefreshTokenRepository"/>.
/// </summary>
public sealed class RefreshTokenRepository : IRefreshTokenRepository
{
    private readonly AuthDbContext _dbContext;

    public RefreshTokenRepository(AuthDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<RefreshToken?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        return _dbContext.RefreshTokens
            .FirstOrDefaultAsync(t => t.TokenHash == tokenHash, cancellationToken);
    }

    public async Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        await _dbContext.RefreshTokens.AddAsync(refreshToken, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryRotateAsync(
        RefreshToken current,
        RefreshToken replacement,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(replacement);

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockFamilyAsync(current.FamilyId, cancellationToken);

        // Conditional UPDATE: only one of several concurrent refreshes sees the row un-revoked.
        var currentId = current.Id;
        DateTimeOffset? revokedAt = now;
        var revoked = await _dbContext.RefreshTokens
            .Where(t => t.Id == currentId && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(t => t.RevokedAt, revokedAt),
                cancellationToken);

        if (revoked == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        await _dbContext.RefreshTokens.AddAsync(replacement, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task RevokeFamilyAsync(Guid familyId, DateTimeOffset now, CancellationToken cancellationToken)
    {
        // Same family lock as a rotation: a logout racing a refresh waits for its commit and then
        // also revokes the replacement token it inserted (audit 2026-11-24, logout-refresh-race).
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await LockFamilyAsync(familyId, cancellationToken);
        DateTimeOffset? revokedAt = now;
        await _dbContext.RefreshTokens
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.RevokedAt, revokedAt), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>Serializes rotation and revocation of one session (held until the transaction ends).</summary>
    private Task LockFamilyAsync(Guid familyId, CancellationToken cancellationToken) =>
        _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({familyId.ToString()}, 0))", cancellationToken);

    public Task<bool> IsSessionActiveAsync(
        Guid userId,
        Guid familyId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        _dbContext.RefreshTokens.AnyAsync(
            t => t.FamilyId == familyId && t.UserId == userId && t.RevokedAt == null && t.ExpiresAt > now,
            cancellationToken);
}
