using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Quadra.Modules.Geo.Places;

/// <summary>
/// <see cref="IPlaceSearchService"/> backed by Photon (https://photon.komoot.io), the public
/// search-as-you-type geocoder over OpenStreetMap data. Needs no key, so the address search
/// works before a Google key exists. Every suggestion already carries its coordinates.
///
/// ponytail: public fair-use server with no SLA — fine for the test environment; move to
/// Google (or a self-hosted Photon) before real traffic.
/// </summary>
public sealed class PhotonPlaceSearchService : IPlaceSearchService
{
    public const string HttpClientName = "photon";

    private const string SearchUrl = "https://photon.komoot.io/api/";
    private const int Limit = 6;

    // ponytail: Brazil only (west,south,east,north); make it configurable when the app leaves the country.
    private const string BrazilBoundingBox = "-73.99,-33.75,-34.79,5.27";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<PhotonPlaceSearchService> _logger;

    public PhotonPlaceSearchService(IHttpClientFactory httpClientFactory, ILogger<PhotonPlaceSearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(
        PlaceSearchQuery query,
        CancellationToken cancellationToken)
    {
        var url = $"{SearchUrl}?q={Uri.EscapeDataString(query.Text)}&limit={Limit}&bbox={BrazilBoundingBox}";
        if (query.Latitude is { } latitude && query.Longitude is { } longitude)
        {
            url += string.Create(CultureInfo.InvariantCulture, $"&lat={latitude}&lon={longitude}");
        }

        try
        {
            using var response = await _httpClientFactory.CreateClient(HttpClientName).GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Photon answered {StatusCode}.", (int)response.StatusCode);
                throw new PlaceSearchUnavailableException($"Photon answered {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            return document.RootElement.TryGetProperty("features", out var features)
                ? features.EnumerateArray().Select(ToSuggestion).OfType<PlaceSuggestion>().ToList()
                : [];
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new PlaceSearchUnavailableException("Photon could not be reached.", ex);
        }
    }

    /// <summary>Photon suggestions always carry coordinates, so there is nothing to resolve.</summary>
    public Task<PlaceDetails?> GetAsync(string placeId, string? sessionToken, CancellationToken cancellationToken) =>
        Task.FromResult<PlaceDetails?>(null);

    private static PlaceSuggestion? ToSuggestion(JsonElement feature)
    {
        if (!feature.TryGetProperty("geometry", out var geometry)
            || !geometry.TryGetProperty("coordinates", out var coordinates)
            || coordinates.GetArrayLength() < 2
            || !feature.TryGetProperty("properties", out var properties))
        {
            return null;
        }

        string? Property(string name) =>
            properties.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        var street = Property("street") is { } streetName
            ? string.Join(", ", new[] { streetName, Property("housenumber") }.Where(part => part is not null))
            : null;
        var title = Property("name") ?? street;
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        var subtitle = string.Join(
            " · ",
            new[] { street, Property("district"), Property("city"), Property("state") }
                .Where(part => !string.IsNullOrWhiteSpace(part) && part != title)
                .Distinct());

        var osmId = properties.TryGetProperty("osm_id", out var id) ? id.ToString() : string.Empty;

        // GeoJSON order: [longitude, latitude].
        return new PlaceSuggestion(
            $"osm:{Property("osm_type")}{osmId}",
            title,
            subtitle,
            coordinates[1].GetDouble(),
            coordinates[0].GetDouble());
    }
}

/// <summary>Search disabled (<c>Places:Provider = None</c>): never calls out, never finds anything.</summary>
public sealed class NoPlaceSearchService : IPlaceSearchService
{
    public Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(PlaceSearchQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PlaceSuggestion>>([]);

    public Task<PlaceDetails?> GetAsync(string placeId, string? sessionToken, CancellationToken cancellationToken) =>
        Task.FromResult<PlaceDetails?>(null);
}
