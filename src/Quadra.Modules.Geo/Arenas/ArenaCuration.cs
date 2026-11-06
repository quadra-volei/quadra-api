using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Npgsql;
using Quadra.Modules.Geo.Application;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Persistence;
using Quadra.Modules.Geo.Validation;
using Quadra.Modules.Geo.Venues;

namespace Quadra.Modules.Geo.Arenas;

/// <summary>
/// The team's arena curation (DECISIONS #94): the pending Google candidates of an area, a decision
/// on each (approve as an arena, reject, or link to an arena), undoing one, and searching Google for
/// an area on demand. Only admins reach it (<c>AuthPolicies.Admin</c>).
/// </summary>
public sealed class ArenaCuration
{
    public const int MaxCandidates = 300;

    /// <summary>Pending places this close to each other are probably the same place.</summary>
    public const double NeighborMeters = 100;

    /// <summary>A game this close to a candidate counts as played there, for the order only.</summary>
    public const double GameMeters = 100;

    private static readonly GeometryFactory Geometry = new(new PrecisionModel(), 4326);

    private readonly GeoDbContext _db;
    private readonly ArenaReader _arenas;
    private readonly VenueDiscovery _discovery;
    private readonly TimeProvider _timeProvider;

    public ArenaCuration(GeoDbContext db, ArenaReader arenas, VenueDiscovery discovery, TimeProvider timeProvider)
    {
        _db = db;
        _arenas = arenas;
        _discovery = discovery;
        _timeProvider = timeProvider;
    }

    /// <summary>Pending candidates of the rectangle (games nearby first, then closest), and its active arenas.</summary>
    public async Task<CurationCandidatesResponse> CandidatesAsync(MapItemsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = _timeProvider.GetUtcNow();
        var fresh = now - VenueDiscovery.MaxAge;
        var startedSince = now - GetMapItemsHandler.StartedGrace;
        var box = Geometry.ToGeometry(new Envelope(query.West, query.East, query.South, query.North));
        var origin = new Point(query.Lon ?? (query.West + query.East) / 2, query.Lat ?? (query.South + query.North) / 2) { SRID = 4326 };

        var pending = _db.Venues.AsNoTracking()
            .Where(v => v.FetchedAt > fresh
                && v.Location.Intersects(box)
                && !_db.ArenaPlaces.Any(p => p.PlaceId == v.PlaceId));
        var pendingCount = await pending.CountAsync(cancellationToken);
        var rows = await pending
            .Select(v => new
            {
                v.PlaceId,
                v.Location,
                v.Kind,
                v.FetchedAt,
                DistanceMeters = v.Location.Distance(origin),
                Games = _db.Matches.Count(m => m.Visibility == "Open"
                    && (m.Status == "InProgress"
                        || ((m.Status == "Draft" || m.Status == "Open" || m.Status == "Closed") && m.DateTime > startedSince))
                    && m.Location.IsWithinDistance(v.Location, GameMeters)),
            })
            .OrderByDescending(v => v.Games)
            .ThenBy(v => v.DistanceMeters)
            .Take(MaxCandidates)
            .ToListAsync(cancellationToken);

        var candidates = rows
            .Select(v => new CurationCandidateResponse(
                v.PlaceId,
                v.Location.Y,
                v.Location.X,
                v.Kind,
                Math.Round(v.DistanceMeters / 1000, 3),
                v.Games,
                // ponytail: every row against every row, fine for the 300 listed; a spatial query if that grows.
                rows.Where(o => o.PlaceId != v.PlaceId && Haversine.Meters(o.Location, v.Location) <= NeighborMeters)
                    .Select(o => o.PlaceId)
                    .ToList(),
                v.FetchedAt + VenueDiscovery.MaxAge))
            .ToList();

        var arenas = await _db.Arenas.AsNoTracking()
            .Where(a => a.Status == ArenaStatuses.Active && a.Location.Intersects(box))
            .OrderBy(a => a.Location.Distance(origin))
            .Take(MaxCandidates)
            .Select(a => new
            {
                a.Id,
                a.Location,
                a.Kind,
                PlaceId = _db.ArenaPlaces
                    .Where(p => p.ArenaId == a.Id && p.Status == ArenaPlaceStatuses.Linked)
                    .OrderByDescending(p => p.IsPrimary)
                    .Select(p => p.PlaceId)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return new CurationCandidatesResponse(
            candidates,
            arenas.Select(a => new CurationArenaResponse(a.Id, a.PlaceId, a.Location.Y, a.Location.X, a.Kind)).ToList(),
            pendingCount,
            pendingCount > MaxCandidates);
    }

    /// <summary>
    /// Applies one decision on a pending candidate. Returns the arena it now belongs to (null when rejected).
    /// </summary>
    /// <exception cref="CurationNotFoundException">No cached candidate with that id, or the arena to link is not active.</exception>
    /// <exception cref="CurationConflictException">The place was already decided.</exception>
    public async Task<Guid?> DecideAsync(Guid reviewerId, CurationDecisionRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _timeProvider.GetUtcNow();
        if (!await _db.Venues.AnyAsync(v => v.PlaceId == request.PlaceId, cancellationToken))
        {
            throw new CurationNotFoundException($"No candidate '{request.PlaceId}'.");
        }

        if (await _db.ArenaPlaces.AnyAsync(p => p.PlaceId == request.PlaceId, cancellationToken))
        {
            throw new CurationConflictException(request.PlaceId);
        }

        Guid? arenaId = null;
        switch (request.Action)
        {
            case CurationActions.Approve:
                var arena = Arena.Create(
                    request.Latitude!.Value,
                    request.Longitude!.Value,
                    request.Kind!,
                    now,
                    request.PinMoved ? ArenaLocationSources.Curator : ArenaLocationSources.Candidate);
                arena.Approve(reviewerId, now);
                _db.Arenas.Add(arena);
                _db.ArenaPlaces.Add(ArenaPlace.Link(request.PlaceId, arena.Id, isPrimary: true, reviewerId, now));
                arenaId = arena.Id;
                break;
            case CurationActions.Link:
                arenaId = await _arenas.FindActiveAsync(request.ArenaId!.Value, cancellationToken)
                    ?? throw new CurationNotFoundException($"No active arena '{request.ArenaId}'.");
                _db.ArenaPlaces.Add(ArenaPlace.Link(request.PlaceId, arenaId.Value, isPrimary: false, reviewerId, now));
                break;
            default:
                _db.ArenaPlaces.Add(ArenaPlace.Reject(request.PlaceId, reviewerId, now));
                break;
        }

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Another admin decided the same place a moment earlier.
            throw new CurationConflictException(request.PlaceId);
        }

        return arenaId;
    }

    /// <summary>
    /// Undoes the decision on a place: it is pending again (while its candidate is cached). An arena
    /// left with no place goes off the map (Inactive), keeping its games' link. False when undecided.
    /// </summary>
    public async Task<bool> UndoAsync(Guid reviewerId, string placeId, CancellationToken cancellationToken)
    {
        var place = await _db.ArenaPlaces.SingleOrDefaultAsync(p => p.PlaceId == placeId, cancellationToken);
        if (place is null)
        {
            return false;
        }

        _db.ArenaPlaces.Remove(place);
        if (place.ArenaId is { } arenaId
            && !await _db.ArenaPlaces.AnyAsync(p => p.ArenaId == arenaId && p.PlaceId != placeId, cancellationToken))
        {
            var arena = await _db.Arenas.SingleAsync(a => a.Id == arenaId, cancellationToken);
            if (arena.Status is ArenaStatuses.Active or ArenaStatuses.Draft)
            {
                arena.Deactivate(reviewerId, _timeProvider.GetUtcNow());
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Searches Google around a point (at most <see cref="VenueDiscovery.MaxCellsPerRequest"/> cells per call).</summary>
    public Task<int> DiscoverAsync(DiscoverCandidatesRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _discovery.DiscoverAroundAsync(request.Lat, request.Lon, request.RadiusKm, cancellationToken);
    }
}

/// <summary>The curation decisions.</summary>
public static class CurationActions
{
    public const string Approve = "Approve";
    public const string Reject = "Reject";
    public const string Link = "Link";
}

/// <summary>A candidate or arena the decision names does not exist (or is not active). Maps to 404.</summary>
public sealed class CurationNotFoundException : Exception
{
    public CurationNotFoundException(string message)
        : base(message) { }
}

/// <summary>The place was already decided; undo it first. Maps to 409.</summary>
public sealed class CurationConflictException : Exception
{
    public CurationConflictException(string placeId)
        : base($"Place '{placeId}' was already decided.") { }
}
