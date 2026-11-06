using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Quadra.Modules.Geo.Arenas;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Places;
using Quadra.Shared.Authorization;

namespace Quadra.Modules.Geo.Controllers;

/// <summary>Arena curation for the team (DECISIONS #94). Admins only.</summary>
[ApiController]
[Route("api/v1/admin/arenas")]
[Authorize(Policy = AuthPolicies.Admin)]
public sealed class CurationController : ControllerBase
{
    private readonly ArenaCuration _curation;

    public CurationController(ArenaCuration curation)
    {
        _curation = curation;
    }

    /// <summary>GET /api/v1/admin/arenas/candidates?south=…&amp;west=…&amp;north=…&amp;east=…&amp;lat=…&amp;lon=…</summary>
    [HttpGet("candidates")]
    public async Task<ActionResult<CurationCandidatesResponse>> GetCandidates(
        [FromQuery] MapItemsQuery query,
        [FromServices] IValidator<MapItemsQuery> validator,
        CancellationToken cancellationToken)
    {
        if (await InvalidAsync(validator, query, cancellationToken) is { } problem)
        {
            return problem;
        }

        return Ok(await _curation.CandidatesAsync(query, cancellationToken));
    }

    /// <summary>POST /api/v1/admin/arenas/decisions — approve, reject or link one candidate.</summary>
    [HttpPost("decisions")]
    public async Task<ActionResult<CurationDecisionResponse>> Decide(
        [FromBody] CurationDecisionRequest request,
        [FromServices] IValidator<CurationDecisionRequest> validator,
        CancellationToken cancellationToken)
    {
        if (await InvalidAsync(validator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        try
        {
            return Ok(new CurationDecisionResponse(await _curation.DecideAsync(CallerId(), request, cancellationToken)));
        }
        catch (CurationNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (CurationConflictException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>DELETE /api/v1/admin/arenas/decisions/{placeId} — undoes a decision.</summary>
    [HttpDelete("decisions/{placeId}")]
    public async Task<IActionResult> Undo(string placeId, CancellationToken cancellationToken) =>
        await _curation.UndoAsync(CallerId(), placeId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>POST /api/v1/admin/arenas/discover — searches Google around a point (costs: Places rate limit).</summary>
    [HttpPost("discover")]
    [EnableRateLimiting(PlacesOptions.RateLimitPolicy)]
    public async Task<ActionResult<DiscoverCandidatesResponse>> Discover(
        [FromBody] DiscoverCandidatesRequest request,
        [FromServices] IValidator<DiscoverCandidatesRequest> validator,
        CancellationToken cancellationToken)
    {
        if (await InvalidAsync(validator, request, cancellationToken) is { } problem)
        {
            return problem;
        }

        return Ok(new DiscoverCandidatesResponse(await _curation.DiscoverAsync(request, cancellationToken)));
    }

    // The admin policy already required a valid account id in the token.
    private Guid CallerId() => Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    private async Task<ActionResult?> InvalidAsync<T>(IValidator<T> validator, T value, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(value, cancellationToken);
        if (validation.IsValid)
        {
            return null;
        }

        foreach (var failure in validation.Errors)
        {
            ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
        }

        return ValidationProblem(ModelState);
    }
}
