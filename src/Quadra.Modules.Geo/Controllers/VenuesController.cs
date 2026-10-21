using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Quadra.Modules.Geo.Application;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Places;

namespace Quadra.Modules.Geo.Controllers;

/// <summary>
/// Arenas on the map (DECISIONS #82). Business logic lives in <see cref="GetNearbyVenuesHandler"/>.
/// Shares the Places rate limit: a new area can trigger Google searches.
/// </summary>
[ApiController]
[Route("api/v1/venues")]
[Authorize]
[EnableRateLimiting(PlacesOptions.RateLimitPolicy)]
public sealed class VenuesController : ControllerBase
{
    private readonly GetNearbyVenuesHandler _handler;
    private readonly IValidator<NearbyMatchesQuery> _validator;

    public VenuesController(GetNearbyVenuesHandler handler, IValidator<NearbyMatchesQuery> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>GET /api/v1/venues/nearby?lat=…&amp;lon=…&amp;radiusKm=…</summary>
    [HttpGet("nearby")]
    public async Task<ActionResult<NearbyVenuesResponse>> GetNearby(
        [FromQuery] NearbyMatchesQuery query,
        CancellationToken cancellationToken)
    {
        var validation = await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        return Ok(await _handler.HandleAsync(query, cancellationToken));
    }
}
