using Microsoft.EntityFrameworkCore;
using Npgsql;
using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>EF Core implementation of <see cref="IPlayerProfileRepository"/>.</summary>
public sealed class PlayerProfileRepository : IPlayerProfileRepository
{
    private readonly ProfileDbContext _context;

    public PlayerProfileRepository(ProfileDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<ProfileWithStats?> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await _context.PlayerProfiles
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (profile is null)
        {
            return null;
        }

        var stats = await _context.PlayerStats
            .FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken)
            ?? PlayerStats.Empty(userId, profile.CreatedAt);

        return new ProfileWithStats(profile, stats);
    }

    /// <inheritdoc/>
    public async Task AddAsync(PlayerProfile profile, PlayerStats stats, CancellationToken cancellationToken)
    {
        await _context.PlayerProfiles.AddAsync(profile, cancellationToken);
        await _context.PlayerStats.AddAsync(stats, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(PlayerProfile profile, CancellationToken cancellationToken)
    {
        _context.PlayerProfiles.Update(profile);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<bool> TryUpdateAsync(PlayerProfile profile, CancellationToken cancellationToken)
    {
        _context.PlayerProfiles.Update(profile);
        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (
            ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return false;
        }
    }

    public async Task<bool> IsHandleTakenAsync(
        string handle,
        Guid exceptUserId,
        CancellationToken cancellationToken)
    {
        return await _context.PlayerProfiles
            .AnyAsync(p => p.Handle == handle && p.UserId != exceptUserId, cancellationToken);
    }

    public async Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await _context.PlayerProfiles
            .AnyAsync(p => p.UserId == userId, cancellationToken);
    }
}
