using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quadra.Modules.Geo.Places;

namespace Quadra.UnitTests.Modules.Geo;

/// <summary>
/// Unit tests for the address-search providers behind <see cref="IPlaceSearchService"/>, with
/// the HTTP transport stubbed: what is sent to each provider and how its answer is read.
/// </summary>
public sealed class PlaceSearchServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // ─── provider selection ──────────────────────────────────────────────────

    /// <summary>
    /// Covers: with no explicit provider, a Google key selects Google and no key selects
    /// OpenStreetMap; Google without a key and unknown names are invalid configuration.
    /// </summary>
    [Theory]
    [InlineData("", "", PlacesOptions.OpenStreetMap, true)]
    [InlineData("", "key", PlacesOptions.Google, true)]
    [InlineData("none", "key", PlacesOptions.None, true)]
    [InlineData("openstreetmap", "key", PlacesOptions.OpenStreetMap, true)]
    [InlineData("Google", "", PlacesOptions.Google, false)]
    [InlineData("Bing", "", null, false)]
    public void Options_resolve_the_provider(string provider, string key, string? expected, bool valid)
    {
        var options = new PlacesOptions { Provider = provider, GoogleApiKey = key };

        options.ResolvedProvider.Should().Be(expected);
        options.IsValid.Should().Be(valid);
    }

    // ─── Google ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: autocomplete POSTs the text, the session token and a location bias with the API
    /// key in a header, and maps predictions to suggestions without coordinates.
    /// </summary>
    [Fact]
    public async Task Google_search_posts_the_query_and_maps_predictions()
    {
        var (sut, handler) = CreateGoogle(HttpStatusCode.OK, """
            {"suggestions":[
              {"placePrediction":{"placeId":"ChIJ_1","text":{"text":"Arena Sky Beach, São Paulo"},
                "structuredFormat":{"mainText":{"text":"Arena Sky Beach"},"secondaryText":{"text":"Pinheiros, São Paulo - SP"}}}},
              {"queryPrediction":{"text":{"text":"arena"}}}
            ]}
            """);

        var result = await sut.SearchAsync(new PlaceSearchQuery("arena sky", -23.55, -46.63, "sess-1"), Ct);

        result.Should().Equal(new PlaceSuggestion("ChIJ_1", "Arena Sky Beach", "Pinheiros, São Paulo - SP", null, null));
        handler.Method.Should().Be(HttpMethod.Post);
        handler.Uri.Should().Be("https://places.googleapis.com/v1/places:autocomplete");
        handler.ApiKey.Should().Be("google-key");
        using var body = JsonDocument.Parse(handler.Body!);
        body.RootElement.GetProperty("input").GetString().Should().Be("arena sky");
        body.RootElement.GetProperty("sessionToken").GetString().Should().Be("sess-1");
        body.RootElement.GetProperty("locationBias").GetProperty("circle").GetProperty("center")
            .GetProperty("latitude").GetDouble().Should().Be(-23.55);
    }

    /// <summary>Covers: details GETs the place with a field mask and returns its coordinates.</summary>
    [Fact]
    public async Task Google_details_returns_the_coordinates()
    {
        var (sut, handler) = CreateGoogle(HttpStatusCode.OK, """
            {"id":"ChIJ_1","displayName":{"text":"Arena Sky Beach"},
             "formattedAddress":"R. dos Pinheiros, 100 - São Paulo","location":{"latitude":-23.56,"longitude":-46.69}}
            """);

        var place = await sut.GetAsync("ChIJ_1", "sess-1", Ct);

        place.Should().Be(new PlaceDetails("ChIJ_1", "Arena Sky Beach", "R. dos Pinheiros, 100 - São Paulo", -23.56, -46.69));
        handler.Uri.Should().Be("https://places.googleapis.com/v1/places/ChIJ_1?languageCode=pt-BR&sessionToken=sess-1");
        handler.FieldMask.Should().Be("id,displayName,formattedAddress,location");
    }

    /// <summary>Covers: an unknown place id is "not found", a provider error is "unavailable".</summary>
    [Fact]
    public async Task Google_maps_unknown_ids_and_failures()
    {
        var (unknown, _) = CreateGoogle(HttpStatusCode.NotFound, "{}");
        (await unknown.GetAsync("nope", null, Ct)).Should().BeNull();

        var (failing, _) = CreateGoogle(HttpStatusCode.Forbidden, """{"error":{"message":"key"}}""");
        var act = async () => await failing.SearchAsync(new PlaceSearchQuery("arena", null, null, null), Ct);
        await act.Should().ThrowAsync<PlaceSearchUnavailableException>();
    }

    // ─── Photon (OpenStreetMap) ──────────────────────────────────────────────

    /// <summary>
    /// Covers: Photon is queried inside Brazil, biased to the user, and its GeoJSON features
    /// become suggestions that already carry coordinates (longitude comes first in GeoJSON).
    /// </summary>
    [Fact]
    public async Task Photon_search_maps_features_with_coordinates()
    {
        var (sut, handler) = CreatePhoton(HttpStatusCode.OK, """
            {"features":[
              {"geometry":{"coordinates":[-46.69,-23.56]},
               "properties":{"osm_type":"W","osm_id":123,"name":"Arena Sky Beach","street":"Rua dos Pinheiros",
                             "housenumber":"100","district":"Pinheiros","city":"São Paulo","state":"São Paulo"}},
              {"geometry":{"coordinates":[-46.70,-23.57]},
               "properties":{"osm_type":"W","osm_id":456,"street":"Rua Harmonia","city":"São Paulo"}},
              {"geometry":{"coordinates":[-46.71,-23.58]},"properties":{"osm_type":"N","osm_id":789}}
            ]}
            """);

        var result = await sut.SearchAsync(new PlaceSearchQuery("arena sky", -23.55, -46.63, null), Ct);

        result.Should().Equal(
            new PlaceSuggestion("osm:W123", "Arena Sky Beach", "Rua dos Pinheiros, 100 · Pinheiros · São Paulo", -23.56, -46.69),
            new PlaceSuggestion("osm:W456", "Rua Harmonia", "São Paulo", -23.57, -46.70));
        handler.Uri.Should().Be(
            "https://photon.komoot.io/api/?q=arena%20sky&limit=6&bbox=-73.99,-33.75,-34.79,5.27&lat=-23.55&lon=-46.63");
    }

    [Fact]
    public async Task Photon_failure_is_unavailable()
    {
        var (sut, _) = CreatePhoton(HttpStatusCode.ServiceUnavailable, "");

        var act = async () => await sut.SearchAsync(new PlaceSearchQuery("arena", null, null, null), Ct);

        await act.Should().ThrowAsync<PlaceSearchUnavailableException>();
    }

    // ─── helpers ─────────────────────────────────────────────────────────────

    private static (GooglePlaceSearchService Sut, StubHandler Handler) CreateGoogle(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var options = Substitute.For<IOptionsMonitor<PlacesOptions>>();
        options.CurrentValue.Returns(new PlacesOptions { GoogleApiKey = "google-key" });
        var sut = new GooglePlaceSearchService(
            Factory(GooglePlaceSearchService.HttpClientName, handler),
            options,
            NullLogger<GooglePlaceSearchService>.Instance);
        return (sut, handler);
    }

    private static (PhotonPlaceSearchService Sut, StubHandler Handler) CreatePhoton(HttpStatusCode status, string body)
    {
        var handler = new StubHandler(status, body);
        var sut = new PhotonPlaceSearchService(
            Factory(PhotonPlaceSearchService.HttpClientName, handler),
            NullLogger<PhotonPlaceSearchService>.Instance);
        return (sut, handler);
    }

    private static IHttpClientFactory Factory(string name, HttpMessageHandler handler)
    {
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(name).Returns(_ => new HttpClient(handler, disposeHandler: false));
        return factory;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly string _body;

        public StubHandler(HttpStatusCode statusCode, string body)
        {
            _statusCode = statusCode;
            _body = body;
        }

        public HttpMethod? Method { get; private set; }

        public string? Uri { get; private set; }

        public string? Body { get; private set; }

        public string? ApiKey { get; private set; }

        public string? FieldMask { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            Uri = request.RequestUri?.OriginalString;
            ApiKey = request.Headers.TryGetValues("X-Goog-Api-Key", out var key) ? key.Single() : null;
            FieldMask = request.Headers.TryGetValues("X-Goog-FieldMask", out var mask) ? mask.Single() : null;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(_statusCode) { Content = new StringContent(_body) };
        }
    }
}
