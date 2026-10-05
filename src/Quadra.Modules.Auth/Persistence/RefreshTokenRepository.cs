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

    public async Task RevokeAsync(
        RefreshToken refreshToken,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(refreshToken);
        refreshToken.Revoke(now);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
