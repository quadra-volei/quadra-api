using Microsoft.EntityFrameworkCore;
using NetTopologySuite.Geometries;
using Quadra.Modules.Geo.Arenas;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Persistence;
using Quadra.Modules.Geo.Validation;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Geo.Application;

/// <summary>
/// The map and its list (DECISIONS #93): approved arenas in a rectangle with their games, and the
/// public games that have no arena. A game linked to an arena (<c>matches.arena_id</c>, never by
/// distance) shows only under it; a game whose arena is not active shows as a loose game. One
/// window for everything: games being played, and the ones starting from now to
/// <see cref="MapItemsQuery.Days"/> ahead, full ones included.
/// </summary>
public sealed class GetMapItemsHandler
{
    public const int MaxArenas = 300;
    public const int MaxGames = 300;
    public const int MaxNextGames = 3;
    public const int MaxArenaGames = 50;

    /// <summary>
    /// A game whose start time just passed still shows until its organizer starts it or the summary
    /// ends it. ponytail: fixed 2 h; use the game's own duration if long games drop off too early.
    /// </summary>
    public static readonly TimeSpan StartedGrace = TimeSpan.FromHours(2);

    private static readonly GeometryFactory Geometry = new(new PrecisionModel(), 4326);

    private readonly GeoDbContext _db;
    private readonly ArenaReader _arenas;
    private readonly IMatchAvailabilityReader _availability;
    private readonly IMatchCoverReader _covers;
    private readonly TimeProvider _timeProvider;

    public GetMapItemsHandler(
        GeoDbContext db,
        ArenaReader arenas,
        IMatchAvailabilityReader availability,
        IMatchCoverReader covers,
        TimeProvider timeProvider)
    {
        _db = db;
        _arenas = arenas;
        _availability = availability;
        _covers = covers;
        _timeProvider = timeProvider;
    }

    public async Task<MapItemsResponse> HandleAsync(MapItemsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = _timeProvider.GetUtcNow();
        var box = Geometry.ToGeometry(new Envelope(query.West, query.East, query.South, query.North));
        var origin = new Point(query.Lon ?? (query.West + query.East) / 2, query.Lat ?? (query.South + query.North) / 2) { SRID = 4326 };
        var kinds = MapItemsQueryValidator.ParseKinds(query.Kinds);

        var inBox = _db.Arenas.AsNoTracking()
            .Where(a => a.Status == ArenaStatuses.Active && a.Location.Intersects(box));
        if (kinds is not null)
        {
            inBox = inBox.Where(a => kinds.Contains(a.Kind));
        }

        var arenaCount = await inBox.CountAsync(cancellationToken);
        var arenas = await inBox
            .OrderBy(a => a.Location.Distance(origin))
            .Take(MaxArenas)
            .Select(a => new
            {
                a.Id,
                a.Location,
                a.Kind,
                DistanceMeters = a.Location.Distance(origin),
                PlaceId = _db.ArenaPlaces
                    .Where(p => p.ArenaId == a.Id && p.Status == ArenaPlaceStatuses.Linked)
                    .OrderByDescending(p => p.IsPrimary)
                    .Select(p => p.PlaceId)
                    .FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        // Games of an arena merged into a listed one show under the one that stayed.
        var owner = await OwnersAsync(arenas.Select(a => a.Id).ToList(), cancellationToken);
        var ownerIds = owner.Keys.ToList();

        var window = InWindow(now, now.AddDays(query.Days));
        var linked = await window
            .Where(m => m.ArenaId != null && ownerIds.Contains(m.ArenaId.Value))
            .Select(m => new GameRow(m.Id, m.Name, m.DateTime, m.Status, m.MaxPlayers, m.ArenaId))
            .ToListAsync(cancellationToken);

        var looseInBox = window.Where(m => m.Location.Intersects(box)
            && (m.ArenaId == null || !_db.Arenas.Any(a => a.Id == m.ArenaId
                && (a.Status == ArenaStatuses.Active
                    || (a.Status == ArenaStatuses.Merged
                        && _db.Arenas.Any(t => t.Id == a.MergedIntoId && t.Status == ArenaStatuses.Active))))));
        var looseCount = await looseInBox.CountAsync(cancellationToken);
        var loose = await looseInBox
            .OrderBy(m => m.Location.Distance(origin))
            .Take(MaxGames)
            .Select(m => new
            {
                Record = m,
                DistanceMeters = m.Location.Distance(origin),
            })
            .ToListAsync(cancellationToken);

        var ids = linked.Select(g => g.Id).Concat(loose.Select(g => g.Record.Id)).ToArray();
        IReadOnlyDictionary<Guid, MatchAvailability> availability = ids.Length == 0
            ? new Dictionary<Guid, MatchAvailability>()
            : await _availability.GetAvailabilityAsync(ids, cancellationToken);
        IReadOnlyDictionary<Guid, string> covers = loose.Count == 0
            ? new Dictionary<Guid, string>()
            : await _covers.GetCoverUrlsAsync(loose.Select(g => g.Record.Id).ToArray(), cancellationToken);

        var byArena = linked.ToLookup(g => owner[g.ArenaId!.Value]);
        var arenaItems = arenas
            .Select(a =>
            {
                var games = byArena[a.Id].ToList();
                return new MapArenaResponse(
                    a.Id,
                    a.PlaceId,
                    a.Location.Y,
                    a.Location.X,
                    Math.Round(a.DistanceMeters / 1000, 3),
                    a.Kind,
                    games.Count,
                    games.Any(g => g.Status == "InProgress"),
                    Order(games).Take(MaxNextGames).Select(g => ToGame(g, availability)).ToList());
            })
            .ToList();

        var gameItems = loose
            .Select(g => ToCard(g.Record, g.DistanceMeters, availability) with { CoverUrl = covers.GetValueOrDefault(g.Record.Id) })
            .ToList();

        return new MapItemsResponse(
            arenaItems,
            gameItems,
            arenaCount,
            linked.Count + looseCount,
            arenaCount > MaxArenas || looseCount > MaxGames);
    }

    /// <summary>An active arena (following a merge) with all its upcoming public games; null when there is none.</summary>
    public async Task<ArenaDetailResponse?> GetArenaAsync(Guid arenaId, CancellationToken cancellationToken)
    {
        if (await _arenas.FindActiveAsync(arenaId, cancellationToken) is not { } id)
        {
            return null;
        }

        var arena = await _db.Arenas.AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new
            {
                a.Location,
                a.Kind,
                PlaceId = _db.ArenaPlaces
                    .Where(p => p.ArenaId == a.Id && p.Status == ArenaPlaceStatuses.Linked)
                    .OrderByDescending(p => p.IsPrimary)
                    .Select(p => p.PlaceId)
                    .FirstOrDefault(),
            })
            .SingleAsync(cancellationToken);

        var ownerIds = (await OwnersAsync([id], cancellationToken)).Keys.ToList();
        var now = _timeProvider.GetUtcNow();
        var games = await InWindow(now, now.AddYears(1))
            .Where(m => m.ArenaId != null && ownerIds.Contains(m.ArenaId.Value))
            .OrderBy(m => m.DateTime)
            .Take(MaxArenaGames)
            .Select(m => new GameRow(m.Id, m.Name, m.DateTime, m.Status, m.MaxPlayers, m.ArenaId))
            .ToListAsync(cancellationToken);
        IReadOnlyDictionary<Guid, MatchAvailability> availability = games.Count == 0
            ? new Dictionary<Guid, MatchAvailability>()
            : await _availability.GetAvailabilityAsync(games.Select(g => g.Id).ToArray(), cancellationToken);

        return new ArenaDetailResponse(
            id,
            arena.PlaceId,
            arena.Location.Y,
            arena.Location.X,
            arena.Kind,
            Order(games).Select(g => ToGame(g, availability)).ToList());
    }

    /// <summary>Public games being played, or starting between now (less the grace) and <paramref name="until"/>.</summary>
    private IQueryable<NearbyMatchRecord> InWindow(DateTimeOffset now, DateTimeOffset until)
    {
        var startedSince = now - StartedGrace;
        return _db.Matches.AsNoTracking()
            .Where(m => m.Visibility == "Open")
            .Where(m => m.Status == "InProgress"
                || ((m.Status == "Draft" || m.Status == "Open" || m.Status == "Closed")
                    && m.DateTime > startedSince && m.DateTime <= until));
    }

    /// <summary>Each listed arena and every arena merged into one of them, mapped to the listed one.</summary>
    private async Task<Dictionary<Guid, Guid>> OwnersAsync(List<Guid> arenaIds, CancellationToken cancellationToken)
    {
        var owner = arenaIds.ToDictionary(id => id, id => id);
        if (arenaIds.Count == 0)
        {
            return owner;
        }

        var merged = await _db.Arenas.AsNoTracking()
            .Where(a => a.Status == ArenaStatuses.Merged && a.MergedIntoId != null && arenaIds.Contains(a.MergedIntoId.Value))
            .Select(a => new { a.Id, Target = a.MergedIntoId!.Value })
            .ToListAsync(cancellationToken);
        foreach (var m in merged)
        {
            owner[m.Id] = m.Target;
        }

        return owner;
    }

    private static IEnumerable<GameRow> Order(IEnumerable<GameRow> games) =>
        games.OrderByDescending(g => g.Status == "InProgress").ThenBy(g => g.DateTime);

    private static MapArenaGameResponse ToGame(GameRow game, IReadOnlyDictionary<Guid, MatchAvailability> availability)
    {
        var slots = availability.GetValueOrDefault(game.Id);
        return new MapArenaGameResponse(
            game.Id,
            game.Name,
            game.DateTime,
            game.Status == "InProgress",
            slots is not null && slots.OpenSlots <= 0,
            slots?.ConfirmedCount ?? 0,
            game.MaxPlayers);
    }

    private static NearbyMatchResponse ToCard(
        NearbyMatchRecord match,
        double distanceMeters,
        IReadOnlyDictionary<Guid, MatchAvailability> availability)
    {
        var slots = availability.GetValueOrDefault(match.Id);
        return new NearbyMatchResponse(
            Id: match.Id,
            Name: match.Name,
            Address: match.Address,
            Latitude: match.Location.Y,
            Longitude: match.Location.X,
            DistanceKm: Math.Round(distanceMeters / 1000, 3),
            DateTime: match.DateTime,
            MaxPlayers: match.MaxPlayers,
            OpenSlots: slots?.OpenSlots ?? match.MaxPlayers,
            OpenToDropIns: slots?.HasOpenDropInSlot ?? false,
            Price: match.Price,
            Type: match.Type,
            Status: match.Status,
            ConfirmedCount: slots?.ConfirmedCount ?? 0,
            Format: match.Format,
            Level: match.Level);
    }

    private sealed record GameRow(Guid Id, string Name, DateTimeOffset DateTime, string Status, short MaxPlayers, Guid? ArenaId);
}
