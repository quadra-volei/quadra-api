using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quadra.IntegrationTests.Matches;
using Quadra.Modules.Geo.Places;

namespace Quadra.IntegrationTests.Geo;

/// <summary>
/// HTTP tests for the address search proxy (<c>/api/v1/places</c>) with the provider replaced
/// by a fixed in-memory one — no test ever calls Google or OpenStreetMap.
/// </summary>
public sealed class PlacesEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    private readonly MatchesWebApplicationFactory _fx;

    public PlacesEndpointTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HttpClient Client(IPlaceSearchService places, bool authenticated = true)
    {
        var factory = _fx.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPlaceSearchService>();
            services.AddSingleton(places);
        }));
        var client = factory.CreateClient();
        if (authenticated)
        {
            client.DefaultRequestHeaders.Authorization =
                _fx.CreateAuthenticatedClient(Guid.NewGuid()).DefaultRequestHeaders.Authorization;
        }

        return client;
    }

    [Fact]
    public async Task Autocomplete_returns_the_provider_suggestions()
    {
        var places = new FixedPlaces();

        var response = await Client(places).GetAsync(
            "/api/v1/places/autocomplete?q=%20arena%20sky%20&lat=-23.55&lon=-46.63&sessionToken=sess-1", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("items");
        items.GetArrayLength().Should().Be(2);
        items[0].GetProperty("id").GetString().Should().Be("with-coords");
        items[0].GetProperty("latitude").GetDouble().Should().Be(-23.56);
        items[1].GetProperty("latitude").ValueKind.Should().Be(JsonValueKind.Null);
        places.LastQuery.Should().Be(new PlaceSearchQuery("arena sky", -23.55, -46.63, "sess-1"));
    }

    [Theory]
    [InlineData("/api/v1/places/autocomplete?q=ab")]
    [InlineData("/api/v1/places/autocomplete")]
    [InlineData("/api/v1/places/autocomplete?q=arena&lat=-23.55")]
    [InlineData("/api/v1/places/autocomplete?q=arena&lat=95&lon=10")]
    [InlineData("/api/v1/places/autocomplete?q=arena&sessionToken=bad%20token")]
    public async Task Autocomplete_rejects_invalid_input(string url)
    {
        var response = await Client(new FixedPlaces()).GetAsync(url, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Details_returns_the_place_or_404()
    {
        var client = Client(new FixedPlaces());

        var found = await client.GetAsync("/api/v1/places/needs-details", Ct);
        found.StatusCode.Should().Be(HttpStatusCode.OK);
        (await found.Content.ReadFromJsonAsync<PlaceDetails>(Ct))
            .Should().Be(new PlaceDetails("needs-details", "Quadra do Parque", "Av. do Parque, 10", -23.57, -46.62));

        (await client.GetAsync("/api/v1/places/unknown", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
        // Not shaped like a place id: refused before reaching the provider.
        (await client.GetAsync("/api/v1/places/a%2F..%2Fb", Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Provider_failure_is_503_and_anonymous_is_401()
    {
        var failing = await Client(new FixedPlaces { Fail = true }).GetAsync("/api/v1/places/autocomplete?q=arena", Ct);
        failing.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);

        var anonymous = await Client(new FixedPlaces(), authenticated: false)
            .GetAsync("/api/v1/places/autocomplete?q=arena", Ct);
        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private sealed class FixedPlaces : IPlaceSearchService
    {
        public bool Fail { get; init; }

        public PlaceSearchQuery? LastQuery { get; private set; }

        public Task<IReadOnlyList<PlaceSuggestion>> SearchAsync(PlaceSearchQuery query, CancellationToken cancellationToken)
        {
            if (Fail)
            {
                throw new PlaceSearchUnavailableException("down");
            }

            LastQuery = query;
            return Task.FromResult<IReadOnlyList<PlaceSuggestion>>(
            [
                new PlaceSuggestion("with-coords", "Arena Sky Beach", "Pinheiros · São Paulo", -23.56, -46.69),
                new PlaceSuggestion("needs-details", "Quadra do Parque", "São Paulo", null, null),
            ]);
        }

        public Task<PlaceDetails?> GetAsync(string placeId, string? sessionToken, CancellationToken cancellationToken) =>
            Task.FromResult(placeId == "needs-details"
                ? new PlaceDetails(placeId, "Quadra do Parque", "Av. do Parque, 10", -23.57, -46.62)
                : null);
    }
}
