using NetTopologySuite.Geometries;
using Quadra.Modules.Geo.Application;

namespace Quadra.Modules.Geo.Persistence;

/// <summary>
/// Read-only spatial query over the Matches-owned <c>matches</c> table.
/// </summary>
public interface INearbyMatchRepository
{
    /// <summary>
    /// Returns upcoming, joinable matches within <paramref name="radiusKm"/> of <paramref name="point"/>,
    /// ordered by straight-line distance ascending and capped at a fixed upper bound.
    /// </summary>
    Task<IReadOnlyList<NearbyMatchCandidate>> FindWithinRadiusAsync(
        Point point,
        double radiusKm,
        DateTimeOffset now,
        CancellationToken cancellationToken);
}
