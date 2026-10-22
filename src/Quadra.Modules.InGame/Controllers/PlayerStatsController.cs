using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Contracts;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Controllers;

/// <summary>The per-player actions sheet of a match (<see cref="PlayerStatsHandler"/>).</summary>
[ApiController]
[Route("api/v1/matches/{matchId:guid}/player-stats")]
[Authorize]
public sealed class PlayerStatsController : ControllerBase
{
    private readonly PlayerStatsHandler _handler;
    private readonly IValidator<SavePlayerStatsRequest> _validator;

    public PlayerStatsController(PlayerStatsHandler handler, IValidator<SavePlayerStatsRequest> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>GET /api/v1/matches/{matchId}/player-stats — organizer and participants.</summary>
    [HttpGet]
    public async Task<ActionResult<PlayerStatsResponse>> Get(Guid matchId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await _handler.GetAsync(matchId, callerId, cancellationToken));
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MatchAccessDeniedException)
        {
            return Forbid();
        }
    }

    /// <summary>PUT /api/v1/matches/{matchId}/player-stats — organizer only, the whole sheet.</summary>
    [HttpPut]
    public async Task<ActionResult<PlayerStatsResponse>> Save(
        Guid matchId,
        [FromBody] SavePlayerStatsRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _validator.ValidateAsync(request, cancellationToken);
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
            return Ok(await _handler.SaveAsync(matchId, callerId, request, cancellationToken));
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MatchAccessDeniedException)
        {
            return Forbid();
        }
        catch (StatsPlayerNotParticipantException ex)
        {
            return UnprocessableEntity(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidScoreboardStateException ex)
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
