using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Controllers;

/// <summary>
/// Thin controller for the live scoreboard endpoints.
/// Business logic lives in the handlers; this class only orchestrates and maps HTTP concerns.
/// </summary>
[ApiController]
[Route("api/v1/matches/{matchId:guid}/scoreboard")]
[Authorize]
public sealed class ScoreboardController : ControllerBase
{
    private readonly CreateScoreboardHandler _createHandler;
    private readonly StartScoreboardHandler _startHandler;
    private readonly RecordPointHandler _recordPointHandler;
    private readonly EndScoreboardHandler _endHandler;
    private readonly GetScoreboardHandler _getHandler;
    private readonly IValidator<CreateScoreboardRequest> _createValidator;
    private readonly IValidator<RecordPointRequest> _recordPointValidator;

    public ScoreboardController(
        CreateScoreboardHandler createHandler,
        StartScoreboardHandler startHandler,
        RecordPointHandler recordPointHandler,
        EndScoreboardHandler endHandler,
        GetScoreboardHandler getHandler,
        IValidator<CreateScoreboardRequest> createValidator,
        IValidator<RecordPointRequest> recordPointValidator)
    {
        _createHandler = createHandler;
        _startHandler = startHandler;
        _recordPointHandler = recordPointHandler;
        _endHandler = endHandler;
        _getHandler = getHandler;
        _createValidator = createValidator;
        _recordPointValidator = recordPointValidator;
    }

    /// <summary>POST /api/v1/matches/{matchId}/scoreboard</summary>
    [HttpPost]
    public async Task<ActionResult<ScoreboardResponse>> CreateScoreboard(
        Guid matchId,
        [FromBody] CreateScoreboardRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _createValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var format = Enum.Parse<ScoreboardFormat>(request.Format, ignoreCase: true);

        try
        {
            var response = await _createHandler.HandleAsync(matchId, callerId, format, cancellationToken);
            return CreatedAtAction(nameof(GetScoreboard), new { matchId }, response);
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MatchAccessDeniedException)
        {
            return Forbid();
        }
        catch (TeamsNotFormedException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (ScoreboardAlreadyExistsException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidScoreboardStateException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>POST /api/v1/matches/{matchId}/scoreboard/start</summary>
    [HttpPost("start")]
    public async Task<ActionResult<ScoreboardResponse>> StartScoreboard(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await _startHandler.HandleAsync(matchId, callerId, cancellationToken);
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
        catch (ScoreboardNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidScoreboardStateException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>POST /api/v1/matches/{matchId}/scoreboard/sets/{setNumber}/points</summary>
    [HttpPost("sets/{setNumber:int}/points")]
    public async Task<ActionResult<ScoreboardResponse>> RecordPoint(
        Guid matchId,
        int setNumber,
        [FromBody] RecordPointRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _recordPointValidator.ValidateAsync(request, cancellationToken);
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
            var response = await _recordPointHandler.HandleAsync(
                matchId, setNumber, request.TeamId, callerId, cancellationToken);
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
        catch (ScoreboardNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (TeamNotInScoreboardException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (SetNotCurrentException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidScoreboardStateException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>POST /api/v1/matches/{matchId}/scoreboard/end</summary>
    [HttpPost("end")]
    public async Task<ActionResult<ScoreboardResponse>> EndScoreboard(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await _endHandler.HandleAsync(matchId, callerId, cancellationToken);
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
        catch (ScoreboardNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidScoreboardStateException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>GET /api/v1/matches/{matchId}/scoreboard</summary>
    [HttpGet]
    public async Task<ActionResult<ScoreboardResponse>> GetScoreboard(
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
        catch (ScoreboardNotFoundException ex)
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
