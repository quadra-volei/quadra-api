using Microsoft.EntityFrameworkCore;
using Quadra.Infrastructure.Contracts;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// <see cref="IPlayerSummaryReader"/> backed by <c>player_profiles</c>. Players without a
/// profile row get the placeholder summary.
/// </summary>
public sealed class PlayerSummaryReader : IPlayerSummaryReader
{
    private readonly ProfileDbContext _context;
    private readonly IProfilePhotoStorage _photoStorage;

    public PlayerSummaryReader(ProfileDbContext context, IProfilePhotoStorage photoStorage)
    {
        _context = context;
        _photoStorage = photoStorage;
    }

    public async Task<IReadOnlyDictionary<Guid, PlayerSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playerIds);

        var ids = playerIds.Distinct().ToArray();
        var result = ids.ToDictionary(id => id, NoOpPlayerSummaryReader.Placeholder);
        if (ids.Length == 0)
        {
            return result;
        }

        var profiles = await _context.PlayerProfiles
            .AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .ToListAsync(cancellationToken);

        foreach (var profile in profiles)
        {
            var photoUrl = await GetProfileHandler.ResolvePhotoUrlAsync(
                profile.PhotoObjectKey, _photoStorage, cancellationToken);

            result[profile.UserId] = new PlayerSummary(
                profile.UserId,
                profile.DisplayName,
                profile.Handle,
                profile.Position?.ToString(),
                profile.Level.ToString(),
                photoUrl);
        }

        return result;
    }
}
