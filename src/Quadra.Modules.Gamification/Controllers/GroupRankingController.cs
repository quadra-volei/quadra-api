using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Contracts;
using Quadra.Shared.Contracts;

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
        [FromServices] IMatchAccessReader access,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        if (!await access.CanViewAsync(matchId, callerId, cancellationToken))
        {
            return NotFound();
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

    /// <summary>
    /// GET /api/v1/rankings/mine — the ranking of the group where the caller scored most
    /// recently, with player names. 204 when the caller is in no ranking yet.
    /// </summary>
    [HttpGet("api/v1/rankings/mine")]
    public async Task<ActionResult<GroupRankingResponse>> GetMine(
        [FromServices] GetMyRankingHandler handler,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _validator.ValidateAsync(new GroupRankingQuery(page, pageSize), cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var ranking = await handler.HandleAsync(callerId, page, pageSize, cancellationToken);
        return ranking is null ? NoContent() : Ok(ranking);
    }

    /// <summary>
    /// GET /api/v1/rankings?scope=friends|group|general&amp;groupId=&amp;position= — the season
    /// ranking. 404 when the group does not exist or the caller may not see it.
    /// </summary>
    [HttpGet("api/v1/rankings")]
    public async Task<ActionResult<RankingResponse>> GetSeason(
        [FromQuery] RankingQuery query,
        [FromServices] GetRankingHandler handler,
        [FromServices] IValidator<RankingQuery> validator,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var ranking = await handler.HandleAsync(callerId, query, cancellationToken);
        return ranking is null ? NotFound() : Ok(ranking);
    }

    /// <summary>GET /api/v1/rankings/groups — the recurring games the caller scored in.</summary>
    [HttpGet("api/v1/rankings/groups")]
    public async Task<ActionResult<IReadOnlyList<RankingGroupResponse>>> GetGroups(
        [FromServices] GetRankingHandler handler,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return Ok(await handler.ListGroupsAsync(callerId, cancellationToken));
    }

    private bool TryGetCallerId(out Guid callerId)
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out callerId);
    }
}
