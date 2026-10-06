using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Contracts;

namespace Quadra.Modules.Matches.Controllers;

/// <summary>
/// Thin controller for the immutable match summary endpoints (F1.6).
/// Business logic lives in the handlers; this class only orchestrates and maps HTTP concerns.
/// </summary>
[ApiController]
[Route("api/v1/matches/{matchId:guid}/summary")]
[Authorize]
public sealed class MatchSummaryController : ControllerBase
{
    private readonly GenerateMatchSummaryHandler _generateHandler;
    private readonly GetMatchSummaryHandler _getHandler;

    public MatchSummaryController(
        GenerateMatchSummaryHandler generateHandler,
        GetMatchSummaryHandler getHandler)
    {
        _generateHandler = generateHandler;
        _getHandler = getHandler;
    }

    /// <summary>POST /api/v1/matches/{matchId}/summary</summary>
    [HttpPost]
    public async Task<ActionResult<MatchSummaryResponse>> GenerateSummary(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await _generateHandler.HandleAsync(matchId, callerId, cancellationToken);
            return CreatedAtAction(nameof(GetSummary), new { matchId }, response);
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MatchAccessDeniedException)
        {
            return Forbid();
        }
        catch (MatchSummaryAlreadyExistsException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (ScoreboardNotFoundForSummaryException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (MatchNotEndedException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (MvpVotingStillOpenException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>GET /api/v1/matches/{matchId}/summary</summary>
    [HttpGet]
    public async Task<ActionResult<MatchSummaryResponse>> GetSummary(
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
        catch (MatchSummaryNotFoundException ex)
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
