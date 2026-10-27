using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Contracts;

namespace Quadra.Modules.Gamification.Controllers;

/// <summary>The caller's season pass (DECISIONS #98). Only the caller's own: no route takes a player id.</summary>
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
}
