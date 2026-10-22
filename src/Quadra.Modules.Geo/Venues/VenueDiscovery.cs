using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quadra.Modules.Geo.Persistence;
using Quadra.Modules.Geo.Places;

namespace Quadra.Modules.Geo.Venues;

/// <summary>Where arenas come from: Google Places, or nothing when Google is not configured.</summary>
public interface IVenueSource
{
    /// <summary>Ids of the places matching <paramref name="text"/> inside the rectangle.</summary>
    /// <exception cref="PlaceSearchUnavailableException">The provider failed or timed out.</exception>
    Task<IReadOnlyList<string>> SearchPlaceIdsAsync(
        string text, double south, double west, double north, double east, CancellationToken cancellationToken);

    /// <summary>Coordinates of a place; null when the provider does not know it.</summary>
    /// <exception cref="PlaceSearchUnavailableException">The provider failed or timed out.</exception>
    Task<(double Latitude, double Longitude)?> GetLocationAsync(string placeId, CancellationToken cancellationToken);
}

/// <summary>No Google key: the map shows no arenas (DECISIONS #82).</summary>
public sealed class NoVenueSource : IVenueSource
{
    public Task<IReadOnlyList<string>> SearchPlaceIdsAsync(
        string text, double south, double west, double north, double east, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<string>>([]);

    public Task<(double Latitude, double Longitude)?> GetLocationAsync(string placeId, CancellationToken cancellationToken) =>
        Task.FromResult<(double Latitude, double Longitude)?>(null);
}

/// <summary>
/// Fills the arena cache for the area the map shows (DECISIONS #82): each grid cell not searched in
/// the last <see cref="RefreshAfter"/> is searched on Google (ids only, free), and the arenas whose
/// coordinates are missing or old get them again (the cheapest details call). Google's terms allow
/// caching coordinates for 30 days: <see cref="PurgeExpiredAsync"/> deletes them after
/// <see cref="MaxAge"/>.
/// </summary>
public sealed class VenueDiscovery
{
    /// <summary>A cell (and an arena's coordinates) older than this is looked up again.</summary>
    public static readonly TimeSpan RefreshAfter = TimeSpan.FromDays(25);

    /// <summary>Coordinates older than this are deleted (Google allows 30 days).</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromDays(29);

    /// <summary>Zoomed out further than this, the map shows cached arenas only.</summary>
    public const double MaxRadiusKm = 10;

    /// <summary>
    /// Cells searched per request, so a first look at a new city stays quick.
    /// ponytail: searched inside the map request; move to a background queue if the first look is slow.
    /// </summary>
    public const int MaxCellsPerRequest = 4;

    // Each search says what kind of place it finds (DECISIONS #87).
    private static readonly (string Text, string Kind)[] Queries =
    [
        ("quadra de vôlei", VenueKinds.Court),
        ("arena de vôlei", VenueKinds.Court),
        ("vôlei de praia", VenueKinds.Sand),
        ("quadra de areia vôlei", VenueKinds.Sand),
        ("parque com quadra de vôlei", VenueKinds.Park),
    ];

    private readonly GeoDbContext _db;
    private readonly IVenueSource _source;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<VenueDiscovery> _logger;

    public VenueDiscovery(GeoDbContext db, IVenueSource source, TimeProvider timeProvider, ILogger<VenueDiscovery> logger)
    {
        _db = db;
        _source = source;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task DiscoverAroundAsync(double latitude, double longitude, double radiusKm, CancellationToken cancellationToken)
    {
        if (_source is NoVenueSource || radiusKm > MaxRadiusKm)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var cells = VenueGrid.CellsAround(latitude, longitude, radiusKm);
        var keys = cells.Select(c => c.Key).ToList();
        var fresh = await _db.VenueAreas
            .AsNoTracking()
            .Where(a => keys.Contains(a.Key) && a.DiscoveredAt > now - RefreshAfter)
            .Select(a => a.Key)
            .ToListAsync(cancellationToken);

        foreach (var cell in cells.Where(c => !fresh.Contains(c.Key)).Take(MaxCellsPerRequest))
        {
            try
            {
                await DiscoverCellAsync(cell, now, cancellationToken);
            }
            catch (PlaceSearchUnavailableException ex)
            {
                // The map still shows what is cached; the cell is tried again next time.
                _logger.LogWarning(ex, "Arena discovery stopped at cell {Cell}.", cell.Key);
                return;
            }
        }
    }

    /// <summary>Deletes coordinates older than <see cref="MaxAge"/>. Returns how many arenas went.</summary>
    public Task<int> PurgeExpiredAsync(CancellationToken cancellationToken)
    {
        var limit = _timeProvider.GetUtcNow() - MaxAge;
        return _db.Venues.Where(v => v.FetchedAt < limit).ExecuteDeleteAsync(cancellationToken);
    }

    private async Task DiscoverCellAsync(VenueCell cell, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var searches = Queries.Select(async q =>
            (q.Kind, Ids: await _source.SearchPlaceIdsAsync(q.Text, cell.South, cell.West, cell.North, cell.East, cancellationToken)));
        var found = await Task.WhenAll(searches);
        var kinds = found
            .SelectMany(f => f.Ids.Select(id => (Id: id, f.Kind)))
            .GroupBy(f => f.Id)
            .ToDictionary(g => g.Key, g => g.Select(f => f.Kind).MaxBy(VenueKinds.Rank)!);
        var ids = kinds.Keys.ToList();

        var current = await _db.Venues
            .AsNoTracking()
            .Where(v => ids.Contains(v.PlaceId) && v.FetchedAt > now - RefreshAfter)
            .Select(v => v.PlaceId)
            .ToListAsync(cancellationToken);
        var toFetch = ids.Except(current).ToList();

        // Places already cached only get their kind from this search (no Google call).
        foreach (var id in current)
        {
            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE venues SET kind = {kinds[id]} WHERE place_id = {id}", cancellationToken);
        }

        var locations = await Task.WhenAll(toFetch.Select(id => _source.GetLocationAsync(id, cancellationToken)));

        // ponytail: two people opening the same new area search it twice; the upserts keep one row.
        for (var i = 0; i < toFetch.Count; i++)
        {
            if (locations[i] is not { } location)
            {
                continue;
            }

            await _db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO venues (place_id, location, fetched_at, kind)
                VALUES ({toFetch[i]}, ST_SetSRID(ST_MakePoint({location.Longitude}, {location.Latitude}), 4326)::geography, {now}, {kinds[toFetch[i]]})
                ON CONFLICT (place_id) DO UPDATE SET location = EXCLUDED.location, fetched_at = EXCLUDED.fetched_at, kind = EXCLUDED.kind
                """,
                cancellationToken);
        }

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO venue_areas (key, discovered_at) VALUES ({cell.Key}, {now})
            ON CONFLICT (key) DO UPDATE SET discovered_at = EXCLUDED.discovered_at
            """,
            cancellationToken);

        _logger.LogInformation(
            "Arena discovery for cell {Cell}: {Found} found, {Fetched} located.", cell.Key, ids.Count, toFetch.Count);
    }
}
