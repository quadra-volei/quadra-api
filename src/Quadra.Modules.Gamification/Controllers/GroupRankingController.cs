using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Contracts;

namespace Quadra.Modules.Gamification.Controllers;

/// <summary>
/// Thin controller for the group-ranking endpoint. Business logic lives in the handler; this class
/// only orchestrates authentication, validation, and HTTP concern mapping.
/// </summary>
[ApiController]
[Authorize]
public sealed class GroupRankingController : ControllerBase
{
    private readonly GetGroupRankingHandler _handler;
    private readonly IValidator<GroupRankingQuery> _validator;

    public GroupRankingController(
        GetGroupRankingHandler handler,
        IValidator<GroupRankingQuery> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>GET /api/v1/matches/{matchId}/ranking</summary>
    [HttpGet("api/v1/matches/{matchId:guid}/ranking")]
    public async Task<ActionResult<GroupRankingResponse>> GetRanking(
        Guid matchId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var query = new GroupRankingQuery(page, pageSize);
        var validation = await _validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        try
        {
            return Ok(await _handler.HandleAsync(matchId, callerId, page, pageSize, cancellationToken));
        }
        catch (MatchNotFoundForRankingException)
        {
            return NotFound();
        }
        catch (RankingNotAvailableForOneOffException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    private bool TryGetCallerId(out Guid callerId)
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out callerId);
    }
}
