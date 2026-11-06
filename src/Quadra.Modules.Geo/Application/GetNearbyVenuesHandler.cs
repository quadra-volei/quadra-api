using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Persistence;
using Quadra.Modules.Geo.Venues;

namespace Quadra.Modules.Geo.Application;

/// <summary>
/// The arenas on the map of apps from before the map refactor (DECISIONS #82): the cached Google
/// candidates with today's public games, a game belonging to the closest one within
/// <see cref="VenueRadiusMeters"/>. Reads the cache only: Google is searched when the team asks
/// for an area (curation, DECISIONS #94), never on a user's request. Retired with the minimum version.
/// </summary>
public sealed class GetNearbyVenuesHandler
{
    /// <summary>
    /// How far from the arena's point a game still counts as played there.
    /// ponytail: by distance, since a match stores no place id; store it if arenas sit closer than this.
    /// </summary>
    public const double VenueRadiusMeters = 100;

    private const int MaxResults = 200;

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly GeoDbContext _db;
    private readonly TimeProvider _timeProvider;

    public GetNearbyVenuesHandler(GeoDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<NearbyVenuesResponse> HandleAsync(NearbyMatchesQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = _timeProvider.GetUtcNow();
        var point = new Point(query.Lon, query.Lat) { SRID = 4326 };
        var radiusMeters = query.RadiusKm * 1000;
        var freshSince = now - VenueDiscovery.MaxAge;

        var venues = await _db.Venues
            .AsNoTracking()
            .Where(v => v.FetchedAt > freshSince && v.Location.IsWithinDistance(point, radiusMeters))
            .OrderBy(v => v.Location.Distance(point))
            .Take(MaxResults + 1)
            .Select(v => new { v.PlaceId, v.Location, v.Kind, DistanceMeters = v.Location.Distance(point) })
            .ToListAsync(cancellationToken);
        var hasMore = venues.Count > MaxResults;
        if (hasMore)
        {
            venues.RemoveAt(MaxResults);
        }

        if (venues.Count == 0)
        {
            return new NearbyVenuesResponse([], 0);
        }

        var local = TimeZoneInfo.ConvertTime(now, Brasilia);
        var dayStart = new DateTimeOffset(local.Date, local.Offset).ToUniversalTime();
        var dayEnd = dayStart.AddDays(1);

        // Same rule as the match list: private matches are reached by invitation, never shown.
        var matches = await _db.Matches
            .AsNoTracking()
            .Where(m => m.Visibility == "Open")
            .Where(m => m.Location.IsWithinDistance(point, radiusMeters + VenueRadiusMeters))
            .Where(m => m.Status == "InProgress"
                || (m.DateTime >= dayStart && m.DateTime < dayEnd
                    && (m.Status == "Draft" || m.Status == "Open" || m.Status == "Closed")))
            .OrderBy(m => m.DateTime)
            .Select(m => new { m.Id, m.Name, m.DateTime, m.Status, m.Location })
            .ToListAsync(cancellationToken);

        var byVenue = venues.ToDictionary(v => v.PlaceId, _ => new List<VenueMatchResponse>());
        foreach (var match in matches)
        {
            var closest = venues
                .Select(v => (v.PlaceId, Meters: Haversine.Meters(v.Location, match.Location)))
                .Where(c => c.Meters <= VenueRadiusMeters)
                .OrderBy(c => c.Meters)
                .FirstOrDefault();
            if (closest.PlaceId is not null)
            {
                byVenue[closest.PlaceId].Add(new VenueMatchResponse(match.Id, match.Name, match.DateTime, match.Status == "InProgress"));
            }
        }

        var items = venues
            .Select(v => new NearbyVenueResponse(
                v.PlaceId,
                v.Location.Y,
                v.Location.X,
                Math.Round(v.DistanceMeters / 1000, 3),
                byVenue[v.PlaceId],
                v.Kind))
            .ToList();

        return new NearbyVenuesResponse(items, items.Count, hasMore);
    }
}

/// <summary>Distance on the Earth's surface between two SRID 4326 points (X = longitude).</summary>
public static class Haversine
{
    private const double EarthRadiusMeters = 6_371_000;

    public static double Meters(Point a, Point b)
    {
        ArgumentNullException.ThrowIfNull(a);
        ArgumentNullException.ThrowIfNull(b);

        var dLat = ToRadians(b.Y - a.Y);
        var dLon = ToRadians(b.X - a.X);
        var h = Math.Pow(Math.Sin(dLat / 2), 2)
            + (Math.Cos(ToRadians(a.Y)) * Math.Cos(ToRadians(b.Y)) * Math.Pow(Math.Sin(dLon / 2), 2));
        return 2 * EarthRadiusMeters * Math.Asin(Math.Sqrt(h));
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180;
}
