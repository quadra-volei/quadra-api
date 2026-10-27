using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Contracts;

namespace Quadra.Modules.Gamification.Controllers;

/// <summary>The caller's season pass (DECISIONS #98) and a player's card collection (#99).</summary>
[ApiController]
[Authorize]
public sealed class SeasonPassController : ControllerBase
{
    /// <summary>GET /api/v1/season-pass — 204 when no season is set up.</summary>
    [HttpGet("api/v1/season-pass")]
    public async Task<ActionResult<SeasonPassResponse>> Get(
        [FromServices] SeasonPassHandler handler,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var callerId))
        {
            return Unauthorized();
        }

        var pass = await handler.GetAsync(callerId, cancellationToken);
        return pass is null ? NoContent() : Ok(pass);
    }

    /// <summary>
    /// GET /api/v1/card-collection?userId= — a player's season cards (DECISIONS #99), the caller's when
    /// no <paramref name="userId"/>. Any signed-in player may see another's, like their profile and ranking:
    /// only pieces won and whether each card is complete.
    /// </summary>
    [HttpGet("api/v1/card-collection")]
    public async Task<ActionResult<CardCollectionResponse>> GetCollection(
        [FromQuery] Guid? userId,
        [FromServices] SeasonPassHandler handler,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var callerId))
        {
            return Unauthorized();
        }

        return Ok(await handler.GetCollectionAsync(userId ?? callerId, cancellationToken));
    }
}
