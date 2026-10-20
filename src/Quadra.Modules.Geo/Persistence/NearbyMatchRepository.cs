using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Quadra.Modules.Geo.Application;

namespace Quadra.Modules.Geo.Persistence;

/// <summary>
/// EF Core implementation of <see cref="INearbyMatchRepository"/>. Runs the PostGIS
/// <c>ST_DWithin</c> + <c>ST_Distance</c> query over the <c>matches.location</c>
/// <c>geography(Point, 4326)</c> column, backed by the <c>ix_matches_location_gist</c> index.
/// </summary>
public sealed class NearbyMatchRepository : INearbyMatchRepository
{
    /// <summary>
    /// Upper bound on returned rows to keep the response payload bounded. With
    /// <c>radiusKm &lt;= 50</c> and the GIST index this is a safe cap; if hit, the closest 200 win.
    /// </summary>
    private const int MaxResults = 200;

    private readonly GeoReadDbContext _context;

    public NearbyMatchRepository(GeoReadDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<NearbyMatchCandidate>> FindWithinRadiusAsync(
        Point point,
        double radiusKm,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(point);

        var radiusMeters = radiusKm * 1000;

        // Project the raw Point plus the scalar distance. ST_Y/ST_X are undefined for `geography`
        // in PostGIS, so Latitude/Longitude must be read from the Point in memory (mirrors
        // MatchesController.ToResponse, which reads Location.Y/.X on a materialized entity).
        var rows = await _context.Matches
            .AsNoTracking()
            .Where(m => m.Location.IsWithinDistance(point, radiusMeters))
            // Draft = confirmation window not open yet: the match is still worth finding.
            .Where(m => m.Status == "Draft" || m.Status == "Open" || m.Status == "Closed")
            // Private matches are reached by invitation, never listed.
            .Where(m => m.Visibility == "Open")
            .Where(m => m.DateTime > now)
            // Order by the raw spatial expression BEFORE projecting so it translates to
            // ORDER BY ST_Distance(...); ordering by a member of the projected record is untranslatable.
            .OrderBy(m => m.Location.Distance(point))
            .Take(MaxResults)
            .Select(m => new SpatialRow(
                m.Id,
                m.Name,
                m.Address,
                m.Location,
                m.DateTime,
                m.MaxPlayers,
                m.Price,
                m.Type,
                m.Status,
                m.Format,
                m.Level,
                m.Location.Distance(point)))
            .ToListAsync(cancellationToken);

        // Distance-ascending order is preserved by the DB ORDER BY above.
        return rows
            .Select(r => new NearbyMatchCandidate(
                r.Id,
                r.Name,
                r.Address,
                r.Location.Y,
                r.Location.X,
                r.DateTime,
                r.MaxPlayers,
                r.Price,
                r.Type,
                r.Status,
                r.Format,
                r.Level,
                r.DistanceMeters))
            .ToList();
    }

    /// <summary>
    /// Intermediate DB projection carrying the raw <see cref="Point"/> so Latitude/Longitude
    /// can be computed client-side (ST_Y/ST_X are undefined for <c>geography</c>).
    /// </summary>
    private sealed record SpatialRow(
        Guid Id,
        string Name,
        string Address,
        Point Location,
        DateTimeOffset DateTime,
        int MaxPlayers,
        decimal? Price,
        string Type,
        string Status,
        string? Format,
        string? Level,
        double DistanceMeters);
}
