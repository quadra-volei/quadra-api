using Quadra.Modules.Geo.Venues;

namespace Quadra.IntegrationTests.Geo;

/// <summary>Google replaced by a fixed set of places: what each search finds, and how many searches ran.</summary>
internal sealed class FixedVenueSource : IVenueSource
{
    private readonly Dictionary<string, (double Latitude, double Longitude)> _places;
    private readonly Dictionary<string, string> _foundBy;

    /// <param name="foundBy">A place listed here is only found by searches containing that word.</param>
    public FixedVenueSource(
        Dictionary<string, (double Latitude, double Longitude)> places,
        Dictionary<string, string>? foundBy = null)
    {
        _places = places;
        _foundBy = foundBy ?? [];
    }

    private int _searches;

    public int Searches => _searches;

    public Task<IReadOnlyList<string>> SearchPlaceIdsAsync(
        string text, double south, double west, double north, double east, CancellationToken cancellationToken)
    {
        // The three queries of a cell run in parallel.
        Interlocked.Increment(ref _searches);
        IReadOnlyList<string> ids = _places
            .Where(p => p.Value.Latitude >= south && p.Value.Latitude < north
                && p.Value.Longitude >= west && p.Value.Longitude < east
                && (!_foundBy.TryGetValue(p.Key, out var word) || text.Contains(word, StringComparison.Ordinal)))
            .Select(p => p.Key)
            .ToList();
        return Task.FromResult(ids);
    }

    public Task<(double Latitude, double Longitude)?> GetLocationAsync(string placeId, CancellationToken cancellationToken) =>
        Task.FromResult<(double Latitude, double Longitude)?>(_places.TryGetValue(placeId, out var p) ? p : null);
}
