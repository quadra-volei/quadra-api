using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Profile.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Real implementation of <see cref="IPlayerLevelReader"/> (F1.3 contract) backed by the persisted
/// <c>player_profiles.level</c> column. Replaces the F1.3 <c>NoOpPlayerLevelReader</c> so team
/// drafting balances on real levels. Resilient to players with no profile yet — returns
/// <c>"Beginner"</c> for unknown ids.
/// </summary>
public sealed class PlayerLevelReader : IPlayerLevelReader
{
    private readonly ProfileDbContext _context;

    public PlayerLevelReader(ProfileDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyDictionary<Guid, string>> GetPlayerLevelsAsync(
        IReadOnlyList<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playerIds);

        var result = new Dictionary<Guid, string>(playerIds.Count);
        foreach (var id in playerIds)
        {
            result[id] = Entities.PlayerLevel.Beginner.ToString();
        }

        if (playerIds.Count == 0)
        {
            return result;
        }

        var rows = await _context.PlayerProfiles
            .AsNoTracking()
            .Where(p => playerIds.Contains(p.UserId))
            .Select(p => new { p.UserId, p.Level })
            .ToListAsync(cancellationToken);

        foreach (var row in rows)
        {
            result[row.UserId] = row.Level.ToString();
        }

        return result;
    }
}
