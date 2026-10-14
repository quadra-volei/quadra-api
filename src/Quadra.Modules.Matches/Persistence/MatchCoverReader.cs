using Microsoft.EntityFrameworkCore;
using Quadra.Infrastructure.Storage;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Implements <see cref="IMatchCoverReader"/>: reads <c>matches.cover_object_key</c> and turns
/// each key into a read URL through <see cref="IProfilePhotoStorage"/>.
/// </summary>
public sealed class MatchCoverReader : IMatchCoverReader
{
    private readonly MatchesDbContext _context;
    private readonly IProfilePhotoStorage _photoStorage;

    public MatchCoverReader(MatchesDbContext context, IProfilePhotoStorage photoStorage)
    {
        _context = context;
        _photoStorage = photoStorage;
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<Guid, string>> GetCoverUrlsAsync(
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken)
    {
        var urls = new Dictionary<Guid, string>();
        if (matchIds.Count == 0)
        {
            return urls;
        }

        var keys = await _context.Matches
            .AsNoTracking()
            .Where(m => matchIds.Contains(m.Id) && m.CoverObjectKey != null)
            .Select(m => new { m.Id, Key = m.CoverObjectKey! })
            .ToListAsync(cancellationToken);

        foreach (var row in keys)
        {
            if (await _photoStorage.GetReadUrlAsync(row.Key, cancellationToken) is { } url)
            {
                urls[row.Id] = url;
            }
        }

        return urls;
    }
}
