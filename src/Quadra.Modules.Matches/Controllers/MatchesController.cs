using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Controllers;

/// <summary>
/// Thin controller for match lifecycle endpoints. Business logic lives in the handlers.
/// </summary>
[ApiController]
[Route("api/v1/matches")]
[Authorize]
public sealed class MatchesController : ControllerBase
{
    private readonly CreateMatchHandler _createHandler;
    private readonly GetMatchHandler _getHandler;
    private readonly ListMatchesHandler _listHandler;
    private readonly CancelMatchHandler _cancelHandler;
    private readonly IValidator<CreateMatchRequest> _createValidator;
    private readonly IValidator<ListMatchesQuery> _listValidator;

    public MatchesController(
        CreateMatchHandler createHandler,
        GetMatchHandler getHandler,
        ListMatchesHandler listHandler,
        CancelMatchHandler cancelHandler,
        IValidator<CreateMatchRequest> createValidator,
        IValidator<ListMatchesQuery> listValidator)
    {
        _createHandler = createHandler;
        _getHandler = getHandler;
        _listHandler = listHandler;
        _cancelHandler = cancelHandler;
        _createValidator = createValidator;
        _listValidator = listValidator;
    }

    /// <summary>POST /api/v1/matches</summary>
    [HttpPost]
    public async Task<ActionResult<MatchResponse>> Create(
        [FromBody] CreateMatchRequest request,
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

        var command = new CreateMatchCommand(
            OrganizerId: callerId,
            Name: request.Name,
            Description: request.Description,
            Address: request.Address,
            Latitude: request.Latitude,
            Longitude: request.Longitude,
            DateTime: request.DateTime,
            MaxPlayers: request.MaxPlayers,
            RegularSlots: request.RegularSlots,
            Price: request.Price,
            Type: request.Type,
            Frequency: request.Frequency,
            DayOfWeek: request.DayOfWeek,
            WindowOpensAt: request.WindowOpensAt,
            WindowClosesAt: request.WindowClosesAt);

        var match = await _createHandler.HandleAsync(command, cancellationToken);
        var response = ToResponse(match);

        return CreatedAtAction(nameof(GetById), new { id = match.Id }, response);
    }

    /// <summary>GET /api/v1/matches/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MatchResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var match = await _getHandler.HandleAsync(id, cancellationToken);
            return Ok(ToResponse(match));
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>GET /api/v1/matches</summary>
    [HttpGet]
    public async Task<ActionResult<PagedMatchesResponse>> List(
        [FromQuery] ListMatchesQuery query,
        CancellationToken cancellationToken)
    {
        var validation = await _listValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var (items, totalCount) = await _listHandler.HandleAsync(query, cancellationToken);

        var response = new PagedMatchesResponse(
            Items: items.Select(ToResponse).ToList(),
            Page: query.Page,
            PageSize: query.PageSize,
            TotalCount: totalCount);

        return Ok(response);
    }

    /// <summary>DELETE /api/v1/matches/{id}</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            await _cancelHandler.HandleAsync(id, callerId, cancellationToken);
            return NoContent();
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MatchAccessDeniedException)
        {
            return Forbid();
        }
        catch (InvalidMatchStatusTransitionException)
        {
            return Conflict();
        }
    }

    // --- Private helpers ---

    private bool TryGetCallerId(out Guid callerId)
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out callerId);
    }

    private static MatchResponse ToResponse(Match m) =>
        new(
            Id: m.Id,
            OrganizerId: m.OrganizerId,
            Name: m.Name,
            Description: m.Description,
            Address: m.Address,
            Latitude: m.Location.Y,
            Longitude: m.Location.X,
            DateTime: m.DateTime,
            MaxPlayers: m.MaxPlayers,
            RegularSlots: m.RegularSlots,
            DropInSlots: m.DropInSlots,
            Price: m.Price,
            Type: m.Type.ToString(),
            Frequency: m.Frequency?.ToString(),
            DayOfWeek: m.DayOfWeek.HasValue ? (int)m.DayOfWeek.Value : null,
            WindowOpensAt: m.WindowOpensAt,
            WindowClosesAt: m.WindowClosesAt,
            Status: m.Status.ToString(),
            CreatedAt: m.CreatedAt,
            UpdatedAt: m.UpdatedAt);
}
