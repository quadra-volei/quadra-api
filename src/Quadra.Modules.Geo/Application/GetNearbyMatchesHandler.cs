using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Geo.Application;

/// <summary>
/// Orchestrates the nearby-matches query: PostGIS radius query → slot-availability lookup
/// (via <see cref="IMatchAvailabilityReader"/>) → open-slot / DropIn filtering → mapping.
/// The spatial candidates and their occupancy are combined in application code, not via a DB JOIN.
/// </summary>
public sealed class GetNearbyMatchesHandler
{
    private readonly INearbyMatchRepository _repository;
    private readonly IMatchAvailabilityReader _availabilityReader;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<GetNearbyMatchesHandler> _logger;

    public GetNearbyMatchesHandler(
        INearbyMatchRepository repository,
        IMatchAvailabilityReader availabilityReader,
        TimeProvider timeProvider,
        ILogger<GetNearbyMatchesHandler> logger)
    {
        _repository = repository;
        _availabilityReader = availabilityReader;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<NearbyMatchesResponse> HandleAsync(
        NearbyMatchesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var now = _timeProvider.GetUtcNow();

        // PostGIS convention: X = longitude, Y = latitude, SRID 4326.
        var point = new Point(query.Lon, query.Lat) { SRID = 4326 };

        var candidates = await _repository.FindWithinRadiusAsync(
            point, query.RadiusKm, now, cancellationToken);

        if (candidates.Count == 0)
        {
            _logger.LogInformation(
                "Nearby matches query. Lat={Lat} Lon={Lon} RadiusKm={RadiusKm} OpenToDropIns={OpenToDropIns} Count={Count}",
                query.Lat, query.Lon, query.RadiusKm, query.OpenToDropIns, 0);

            return new NearbyMatchesResponse(Array.Empty<NearbyMatchResponse>(), 0);
        }

        var candidateIds = candidates.Select(c => c.Id).ToArray();
        var availability = await _availabilityReader.GetAvailabilityAsync(candidateIds, cancellationToken);

        var items = new List<NearbyMatchResponse>(candidates.Count);

        foreach (var candidate in candidates)
        {
            if (!availability.TryGetValue(candidate.Id, out var avail))
            {
                continue;
            }

            if (avail.OpenSlots <= 0)
            {
                continue;
            }

            if (query.OpenToDropIns && !avail.HasOpenDropInSlot)
            {
                continue;
            }

            items.Add(ToResponse(candidate, avail));
        }

        _logger.LogInformation(
            "Nearby matches query. Lat={Lat} Lon={Lon} RadiusKm={RadiusKm} OpenToDropIns={OpenToDropIns} Count={Count}",
            query.Lat, query.Lon, query.RadiusKm, query.OpenToDropIns, items.Count);

        return new NearbyMatchesResponse(items, items.Count);
    }

    private static NearbyMatchResponse ToResponse(NearbyMatchCandidate candidate, MatchAvailability availability) =>
        new(
            Id: candidate.Id,
            Name: candidate.Name,
            Address: candidate.Address,
            Latitude: candidate.Latitude,
            Longitude: candidate.Longitude,
            DistanceKm: Math.Round(candidate.DistanceMeters / 1000, 3),
            DateTime: candidate.DateTime,
            MaxPlayers: candidate.MaxPlayers,
            OpenSlots: availability.OpenSlots,
            OpenToDropIns: availability.HasOpenDropInSlot,
            Price: candidate.Price,
            Type: candidate.Type,
            Status: candidate.Status,
            ConfirmedCount: availability.ConfirmedCount,
            Format: candidate.Format,
            Level: candidate.Level);
}
