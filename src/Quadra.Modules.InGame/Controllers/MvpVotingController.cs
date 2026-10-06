using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Contracts;

namespace Quadra.Modules.InGame.Controllers;

/// <summary>
/// Thin controller for the post-match MVP voting endpoints.
/// Business logic lives in the handlers; this class only orchestrates and maps HTTP concerns.
/// </summary>
[ApiController]
[Route("api/v1/matches/{matchId:guid}/mvp-voting")]
[Authorize]
public sealed class MvpVotingController : ControllerBase
{
    private readonly OpenMvpVotingHandler _openHandler;
    private readonly CastMvpVoteHandler _castHandler;
    private readonly CloseMvpVotingHandler _closeHandler;
    private readonly GetMvpVotingHandler _getHandler;
    private readonly IValidator<OpenMvpVotingRequest> _openValidator;
    private readonly IValidator<CastMvpVoteRequest> _castValidator;

    public MvpVotingController(
        OpenMvpVotingHandler openHandler,
        CastMvpVoteHandler castHandler,
        CloseMvpVotingHandler closeHandler,
        GetMvpVotingHandler getHandler,
        IValidator<OpenMvpVotingRequest> openValidator,
        IValidator<CastMvpVoteRequest> castValidator)
    {
        _openHandler = openHandler;
        _castHandler = castHandler;
        _closeHandler = closeHandler;
        _getHandler = getHandler;
        _openValidator = openValidator;
        _castValidator = castValidator;
    }

    /// <summary>POST /api/v1/matches/{matchId}/mvp-voting</summary>
    [HttpPost]
    public async Task<ActionResult<MvpVotingResponse>> OpenVoting(
        Guid matchId,
        [FromBody] OpenMvpVotingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _openValidator.ValidateAsync(request, cancellationToken);
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
            var response = await _openHandler.HandleAsync(
                matchId, callerId, request.DeadlineAt, cancellationToken);
            return CreatedAtAction(nameof(GetVoting), new { matchId }, response);
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MatchAccessDeniedException)
        {
            return Forbid();
        }
        catch (MatchNotEndedException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (MvpVotingAlreadyExistsException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>POST /api/v1/matches/{matchId}/mvp-voting/votes</summary>
    [HttpPost("votes")]
    public async Task<ActionResult<MvpVoteResponse>> CastVote(
        Guid matchId,
        [FromBody] CastMvpVoteRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _castValidator.ValidateAsync(request, cancellationToken);
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
            var response = await _castHandler.HandleAsync(
                matchId, callerId, request.VotedPlayerId, cancellationToken);
            return Ok(response);
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MvpVotingNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (VotedPlayerNotParticipantException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (NotAParticipantException ex)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new ProblemDetails { Detail = ex.Message });
        }
        catch (SelfVoteNotAllowedException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (VotingDeadlinePassedException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidMvpVotingStateException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>POST /api/v1/matches/{matchId}/mvp-voting/close</summary>
    [HttpPost("close")]
    public async Task<ActionResult<MvpVotingResponse>> CloseVoting(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await _closeHandler.HandleAsync(matchId, callerId, cancellationToken);
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
        catch (MvpVotingNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (InvalidMvpVotingStateException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>GET /api/v1/matches/{matchId}/mvp-voting</summary>
    [HttpGet]
    public async Task<ActionResult<MvpVotingResponse>> GetVoting(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            var response = await _getHandler.HandleAsync(matchId, callerId, cancellationToken);
            return Ok(response);
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MvpVotingNotFoundException ex)
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
