using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Contracts;

namespace Quadra.Modules.InGame.Controllers;

/// <summary>
/// Thin controller for team lifecycle endpoints.
/// Business logic lives in the handlers; this class only orchestrates and maps HTTP concerns.
/// </summary>
[ApiController]
[Route("api/v1/matches/{matchId:guid}/teams")]
[Authorize]
public sealed class TeamsController : ControllerBase
{
    private readonly DraftTeamsHandler _draftHandler;
    private readonly GetTeamsHandler _getHandler;
    private readonly MovePlayerHandler _moveHandler;

    public TeamsController(
        DraftTeamsHandler draftHandler,
        GetTeamsHandler getHandler,
        MovePlayerHandler moveHandler)
    {
        _draftHandler = draftHandler;
        _getHandler = getHandler;
        _moveHandler = moveHandler;
    }

    /// <summary>POST /api/v1/matches/{matchId}/teams/draft</summary>
    [HttpPost("draft")]
    public async Task<ActionResult<TeamsResponse>> DraftTeams(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await _draftHandler.HandleAsync(matchId, callerId, cancellationToken);
            return Ok(response);
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MatchAccessDeniedException)
        {
            return Forbid();
        }
        catch (InvalidMatchStatusForTeamsException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (NoConfirmedPlayersException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>GET /api/v1/matches/{matchId}/teams</summary>
    [HttpGet]
    public async Task<ActionResult<TeamsResponse>> GetTeams(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _getHandler.HandleAsync(matchId, cancellationToken);
            return Ok(response);
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (TeamsNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>PUT /api/v1/matches/{matchId}/teams/{teamId}/members/{playerId}</summary>
    [HttpPut("{teamId:guid}/members/{playerId:guid}")]
    public async Task<ActionResult<TeamsResponse>> MovePlayer(
        Guid matchId,
        Guid teamId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await _moveHandler.HandleAsync(matchId, teamId, playerId, callerId, cancellationToken);
            return Ok(response);
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MatchAccessDeniedException)
        {
            return Forbid();
        }
        catch (InvalidMatchStatusForTeamsException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (TeamsNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (TeamNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (PlayerNotInTeamException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
    }

    // --- Private helpers ---

    private bool TryGetCallerId(out Guid callerId)
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out callerId);
    }
}
