using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Geo.Arenas;

/// <summary>
/// The curated arenas as other modules and the create-match form see them: only Active ones, and a
/// merged arena answers with the one that stayed.
/// </summary>
public sealed class ArenaReader : IArenaReader
{
    /// <summary>How close an approved arena must be to a picked spot to be offered for it.</summary>
    public const double SuggestMeters = 100;

    // ponytail: follows a few merges at most; curation re-points older ones when it merges again.
    private const int MaxMergeHops = 5;

    private readonly GeoDbContext _db;

    public ArenaReader(GeoDbContext db)
    {
        _db = db;
    }

    public async Task<Guid?> FindActiveAsync(Guid arenaId, CancellationToken cancellationToken)
    {
        Guid? id = arenaId;
        for (var hop = 0; hop < MaxMergeHops && id is { } current; hop++)
        {
            var arena = await _db.Arenas.AsNoTracking()
                .Where(a => a.Id == current)
                .Select(a => new { a.Status, a.MergedIntoId })
                .SingleOrDefaultAsync(cancellationToken);
            if (arena?.Status == ArenaStatuses.Active)
            {
                return current;
            }

            id = arena?.Status == ArenaStatuses.Merged ? arena.MergedIntoId : null;
        }

        return null;
    }

    public async Task<Guid?> FindActiveByPlaceAsync(string placeId, CancellationToken cancellationToken)
    {
        var arenaId = await _db.ArenaPlaces.AsNoTracking()
            .Where(p => p.PlaceId == placeId && p.Status == ArenaPlaceStatuses.Linked)
            .Select(p => p.ArenaId)
            .SingleOrDefaultAsync(cancellationToken);
        return arenaId is { } id ? await FindActiveAsync(id, cancellationToken) : null;
    }

    /// <summary>
    /// The arenas a game picked at this spot may be at (<c>GET /arenas/near</c>): the one
    /// <paramref name="placeId"/> is linked to first (<c>Linked</c>, the API links it on its own),
    /// then the approved arenas within <see cref="SuggestMeters"/>, closest first, at most 3.
    /// </summary>
    public async Task<NearbyArenasResponse> NearAsync(
        double latitude,
        double longitude,
        string? placeId,
        CancellationToken cancellationToken)
    {
        var point = new Point(longitude, latitude) { SRID = 4326 };
        var linked = string.IsNullOrWhiteSpace(placeId) ? null : await FindActiveByPlaceAsync(placeId, cancellationToken);

        var rows = await _db.Arenas.AsNoTracking()
            .Where(a => a.Status == ArenaStatuses.Active
                && (a.Id == linked || a.Location.IsWithinDistance(point, SuggestMeters)))
            .OrderBy(a => a.Location.Distance(point))
            .Take(4)
            .Select(a => new
            {
                a.Id,
                a.Kind,
                DistanceMeters = a.Location.Distance(point),
                // The main entry names the arena (GET /places/{id}); any linked one when none is marked.
                PlaceId = _db.ArenaPlaces
                    .Where(p => p.ArenaId == a.Id && p.Status == ArenaPlaceStatuses.Linked)
                    .OrderByDescending(p => p.IsPrimary)
                    .Select(p => p.PlaceId)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var items = rows
            .OrderByDescending(r => r.Id == linked)
            .Take(3)
            .Select(r => new NearbyArenaResponse(r.Id, r.PlaceId, (int)Math.Round(r.DistanceMeters), r.Kind, r.Id == linked))
            .ToList();
        return new NearbyArenasResponse(items);
    }
}
