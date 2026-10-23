using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Geo.Application;
using Quadra.Modules.Geo.Contracts;

namespace Quadra.Modules.Geo.Controllers;

/// <summary>The map and its list (DECISIONS #93). Reads only our database: no Google call.</summary>
[ApiController]
[Route("api/v1/map")]
[Authorize]
public sealed class MapController : ControllerBase
{
    private readonly GetMapItemsHandler _handler;
    private readonly IValidator<MapItemsQuery> _validator;

    public MapController(GetMapItemsHandler handler, IValidator<MapItemsQuery> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>GET /api/v1/map/items?south=…&amp;west=…&amp;north=…&amp;east=…&amp;days=…&amp;kinds=…&amp;lat=…&amp;lon=…</summary>
    [HttpGet("items")]
    public async Task<ActionResult<MapItemsResponse>> GetItems(
        [FromQuery] MapItemsQuery query,
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
