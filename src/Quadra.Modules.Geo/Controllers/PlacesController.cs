using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Geo.Places;

namespace Quadra.Modules.Geo.Controllers;

/// <summary>
/// Address / venue search for the create-match form. A thin proxy over
/// <see cref="IPlaceSearchService"/>: the provider key stays on the server.
/// </summary>
[ApiController]
[Route("api/v1/places")]
[Authorize]
public sealed partial class PlacesController : ControllerBase
{
    public const int MinQueryLength = 3;
    public const int MaxQueryLength = 120;

    private readonly IPlaceSearchService _places;

    public PlacesController(IPlaceSearchService places)
    {
        _places = places;
    }

    /// <summary>GET /api/v1/places/autocomplete?q=…&amp;lat=…&amp;lon=…&amp;sessionToken=…</summary>
    [HttpGet("autocomplete")]
    public async Task<ActionResult<PlaceSuggestionsResponse>> Autocomplete(
        [FromQuery] string? q,
        [FromQuery] double? lat,
        [FromQuery] double? lon,
        [FromQuery] string? sessionToken,
        CancellationToken cancellationToken)
    {
        var text = q?.Trim() ?? string.Empty;
        if (text.Length is < MinQueryLength or > MaxQueryLength)
        {
            ModelState.AddModelError(nameof(q), $"q must have {MinQueryLength} to {MaxQueryLength} characters.");
        }

        var hasPoint = lat is >= -90 and <= 90 && lon is >= -180 and <= 180;
        if ((lat.HasValue || lon.HasValue) && !hasPoint)
        {
            ModelState.AddModelError(nameof(lat), "lat and lon must be sent together and be valid coordinates.");
        }

        if (!IsValidToken(sessionToken))
        {
            ModelState.AddModelError(nameof(sessionToken), "sessionToken is not valid.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        try
        {
            var items = await _places.SearchAsync(
                new PlaceSearchQuery(text, hasPoint ? lat : null, hasPoint ? lon : null, sessionToken),
                cancellationToken);
            return Ok(new PlaceSuggestionsResponse(items));
        }
        catch (PlaceSearchUnavailableException)
        {
            return Unavailable();
        }
    }

    /// <summary>GET /api/v1/places/{placeId}?sessionToken=… — coordinates of a picked suggestion.</summary>
    [HttpGet("{placeId}")]
    public async Task<ActionResult<PlaceDetails>> Get(
        string placeId,
        [FromQuery] string? sessionToken,
        CancellationToken cancellationToken)
    {
        // The id goes into the provider's URL: accept only what a place id looks like.
        if (!PlaceIdPattern().IsMatch(placeId) || !IsValidToken(sessionToken))
        {
            return NotFound();
        }

        try
        {
            var place = await _places.GetAsync(placeId, sessionToken, cancellationToken);
            return place is null ? NotFound() : Ok(place);
        }
        catch (PlaceSearchUnavailableException)
        {
            return Unavailable();
        }
    }

    private ObjectResult Unavailable() =>
        Problem(
            statusCode: StatusCodes.Status503ServiceUnavailable,
            detail: "The address search is unavailable right now.");

    private static bool IsValidToken(string? sessionToken) =>
        sessionToken is null || TokenPattern().IsMatch(sessionToken);

    [GeneratedRegex("^[A-Za-z0-9_:-]{1,300}$")]
    private static partial Regex PlaceIdPattern();

    [GeneratedRegex("^[A-Za-z0-9_-]{1,64}$")]
    private static partial Regex TokenPattern();
}

public sealed record PlaceSuggestionsResponse(IReadOnlyList<PlaceSuggestion> Items);
