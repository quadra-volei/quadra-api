using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Geo.Application;
using Quadra.Modules.Geo.Arenas;
using Quadra.Modules.Geo.Contracts;

namespace Quadra.Modules.Geo.Controllers;

/// <summary>Curated arenas (DECISIONS #92, #93). Reads only our database: no Google call.</summary>
[ApiController]
[Route("api/v1/arenas")]
[Authorize]
public sealed class ArenasController : ControllerBase
{
    private readonly ArenaReader _arenas;
    private readonly GetMapItemsHandler _map;

    public ArenasController(ArenaReader arenas, GetMapItemsHandler map)
    {
        _arenas = arenas;
        _map = map;
    }

    /// <summary>GET /api/v1/arenas/{id}: an active arena with its upcoming public games (a merged id answers with the one that stayed).</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ArenaDetailResponse>> GetById(Guid id, CancellationToken cancellationToken) =>
        await _map.GetArenaAsync(id, cancellationToken) is { } arena ? Ok(arena) : NotFound();

    /// <summary>GET /api/v1/arenas/near?lat=…&amp;lon=…&amp;placeId=…</summary>
    [HttpGet("near")]
    public async Task<ActionResult<NearbyArenasResponse>> GetNear(
        [FromQuery] double lat,
        [FromQuery] double lon,
        [FromQuery] string? placeId,
        CancellationToken cancellationToken)
    {
        if (lat is < -90 or > 90)
        {
            ModelState.AddModelError(nameof(lat), "Lat must be between -90 and 90.");
        }

        if (lon is < -180 or > 180)
        {
            ModelState.AddModelError(nameof(lon), "Lon must be between -180 and 180.");
        }

        if (placeId?.Length > 255)
        {
            ModelState.AddModelError(nameof(placeId), "PlaceId must be at most 255 characters.");
        }

        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        return Ok(await _arenas.NearAsync(lat, lon, placeId, cancellationToken));
    }
}
