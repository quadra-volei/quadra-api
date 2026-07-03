using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Geo.Application;
using Quadra.Modules.Geo.Contracts;

namespace Quadra.Modules.Geo.Controllers;

/// <summary>
/// Thin controller for the matches map. Business logic lives in <see cref="GetNearbyMatchesHandler"/>.
/// </summary>
[ApiController]
[Route("api/v1/matches/nearby")]
[Authorize]
public sealed class MatchesMapController : ControllerBase
{
    private readonly GetNearbyMatchesHandler _handler;
    private readonly IValidator<NearbyMatchesQuery> _validator;

    public MatchesMapController(
        GetNearbyMatchesHandler handler,
        IValidator<NearbyMatchesQuery> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>GET /api/v1/matches/nearby</summary>
    [HttpGet]
    public async Task<ActionResult<NearbyMatchesResponse>> GetNearby(
        [FromQuery] NearbyMatchesQuery query,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out _))
        {
            return Unauthorized();
        }

        var validation = await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var response = await _handler.HandleAsync(query, cancellationToken);
        return Ok(response);
    }

    private bool TryGetCallerId(out Guid callerId)
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out callerId);
    }
}
