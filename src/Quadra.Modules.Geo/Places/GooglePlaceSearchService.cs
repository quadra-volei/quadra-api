using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quadra.Modules.Geo.Venues;

namespace Quadra.Modules.Geo.Places;

/// <summary>
/// <see cref="IPlaceSearchService"/> backed by the Google Places API (New), called through
/// <see cref="HttpClient"/> (no SDK). Suggestions come without coordinates; the details call
/// that closes the session supplies them.
/// </summary>
public sealed class GooglePlaceSearchService : IPlaceSearchService, IVenueSource
{
    public const string HttpClientName = "google-places";

    private const string BaseUrl = "https://places.googleapis.com/v1/";
    private const string DetailsFieldMask = "id,displayName,formattedAddress,location";
    private const double BiasRadiusMeters = 50_000;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<PlacesOptions> _options;
    private readonly ILogger<GooglePlaceSearchService> _logger;

    public GooglePlaceSearchService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<PlacesOptions> options,
        ILogger<GooglePlaceSearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options;
        _logger = logger;
    }

    public async Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(
        PlaceSearchQuery query,
        CancellationToken cancellationToken)
    {
        var body = new Dictionary<string, object>(StringComparer.Ordinal)
        {
            ["input"] = query.Text,
            ["languageCode"] = "pt-BR",
            // ponytail: Brazil only; make it configurable when the app leaves the country.
            ["includedRegionCodes"] = new[] { "br" },
        };
        if (query.SessionToken is not null)
        {
            body["sessionToken"] = query.SessionToken;
        }

        if (query.Latitude is { } latitude && query.Longitude is { } longitude)
        {
            body["locationBias"] = new
            {
                circle = new { center = new { latitude, longitude }, radius = BiasRadiusMeters },
            };
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "places:autocomplete")
        {
            Content = JsonContent.Create(body),
        };
        using var document = await SendAsync(request, cancellationToken);
        if (document is null || !document.RootElement.TryGetProperty("suggestions", out var suggestions))
        {
            return [];
        }

        var result = new List<PlaceSuggestion>();
        foreach (var suggestion in suggestions.EnumerateArray())
        {
            if (!suggestion.TryGetProperty("placePrediction", out var prediction)
                || Text(prediction, "placeId") is not { Length: > 0 } placeId)
            {
                continue;
            }

            var format = prediction.TryGetProperty("structuredFormat", out var structured) ? structured : default;
            var title = NestedText(format, "mainText") ?? NestedText(prediction, "text") ?? string.Empty;
            result.Add(new PlaceSuggestion(placeId, title, NestedText(format, "secondaryText") ?? string.Empty, null, null));
        }

        return result;
    }

    public async Task<PlaceDetails?> GetAsync(string placeId, string? sessionToken, CancellationToken cancellationToken)
    {
        var url = $"{BaseUrl}places/{Uri.EscapeDataString(placeId)}?languageCode=pt-BR";
        if (sessionToken is not null)
        {
            url += "&sessionToken=" + Uri.EscapeDataString(sessionToken);
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Add("X-Goog-FieldMask", DetailsFieldMask);
        using var document = await SendAsync(request, cancellationToken);
        if (document is null || !document.RootElement.TryGetProperty("location", out var location))
        {
            return null;
        }

        var root = document.RootElement;
        var address = Text(root, "formattedAddress") ?? string.Empty;
        return new PlaceDetails(
            placeId,
            NestedText(root, "displayName") ?? address,
            address,
            location.GetProperty("latitude").GetDouble(),
            location.GetProperty("longitude").GetDouble());
    }

    /// <summary>
    /// Arena discovery (DECISIONS #82): the ids of the places matching <paramref name="text"/>
    /// inside the rectangle. Asking for the id only keeps the call in Google's free tier.
    /// </summary>
    public async Task<IReadOnlyList<string>> SearchPlaceIdsAsync(
        string text,
        double south,
        double west,
        double north,
        double east,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            textQuery = text,
            languageCode = "pt-BR",
            regionCode = "br",
            pageSize = 20,
            locationRestriction = new
            {
                rectangle = new
                {
                    low = new { latitude = south, longitude = west },
                    high = new { latitude = north, longitude = east },
                },
            },
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "places:searchText")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Goog-FieldMask", "places.id");
        using var document = await SendAsync(request, cancellationToken);
        if (document is null || !document.RootElement.TryGetProperty("places", out var places))
        {
            return [];
        }

        return places.EnumerateArray()
            .Select(p => Text(p, "id"))
            .OfType<string>()
            .ToList();
    }

    /// <summary>Only the coordinates of a place (the cheapest details call); null when unknown.</summary>
    public async Task<(double Latitude, double Longitude)?> GetLocationAsync(
        string placeId,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}places/{Uri.EscapeDataString(placeId)}");
        request.Headers.Add("X-Goog-FieldMask", "location");
        using var document = await SendAsync(request, cancellationToken);
        if (document is null || !document.RootElement.TryGetProperty("location", out var location))
        {
            return null;
        }

        return (location.GetProperty("latitude").GetDouble(), location.GetProperty("longitude").GetDouble());
    }

    /// <summary>Sends the request with the API key; null for 404 / 400 (unknown place id).</summary>
    private async Task<JsonDocument?> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Add("X-Goog-Api-Key", _options.CurrentValue.GoogleApiKey);
        try
        {
            using var response = await _httpClientFactory
                .CreateClient(HttpClientName)
                .SendAsync(request, cancellationToken);

            if (request.Method == HttpMethod.Get
                && response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                // The body may echo the request; only the status is logged.
                _logger.LogWarning("Google Places answered {StatusCode}.", (int)response.StatusCode);
                throw new PlaceSearchUnavailableException($"Google Places answered {(int)response.StatusCode}.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
            || (ex is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new PlaceSearchUnavailableException("Google Places could not be reached.", ex);
        }
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    /// <summary>Reads <c>{ property: { text: "…" } }</c>, the shape Google uses for localized text.</summary>
    private static string? NestedText(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var nested)
            ? Text(nested, "text")
            : null;
}
