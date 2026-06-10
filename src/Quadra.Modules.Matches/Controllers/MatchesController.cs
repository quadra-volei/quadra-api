using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
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
    private readonly AddPresenceHandler _addPresenceHandler;
    private readonly UpdateMyPresenceHandler _updateMyPresenceHandler;
    private readonly GetPresenceListHandler _getPresenceListHandler;
    private readonly RemovePresenceHandler _removePresenceHandler;
    private readonly IValidator<CreateMatchRequest> _createValidator;
    private readonly IValidator<ListMatchesQuery> _listValidator;
    private readonly IValidator<AddPresenceRequest> _addPresenceValidator;
    private readonly IValidator<UpdateMyPresenceRequest> _updateMyPresenceValidator;

    public MatchesController(
        CreateMatchHandler createHandler,
        GetMatchHandler getHandler,
        ListMatchesHandler listHandler,
        CancelMatchHandler cancelHandler,
        AddPresenceHandler addPresenceHandler,
        UpdateMyPresenceHandler updateMyPresenceHandler,
        GetPresenceListHandler getPresenceListHandler,
        RemovePresenceHandler removePresenceHandler,
        IValidator<CreateMatchRequest> createValidator,
        IValidator<ListMatchesQuery> listValidator,
        IValidator<AddPresenceRequest> addPresenceValidator,
        IValidator<UpdateMyPresenceRequest> updateMyPresenceValidator)
    {
        _createHandler = createHandler;
        _getHandler = getHandler;
        _listHandler = listHandler;
        _cancelHandler = cancelHandler;
        _addPresenceHandler = addPresenceHandler;
        _updateMyPresenceHandler = updateMyPresenceHandler;
        _getPresenceListHandler = getPresenceListHandler;
        _removePresenceHandler = removePresenceHandler;
        _createValidator = createValidator;
        _listValidator = listValidator;
        _addPresenceValidator = addPresenceValidator;
        _updateMyPresenceValidator = updateMyPresenceValidator;
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

    /// <summary>POST /api/v1/matches/{id}/presences</summary>
    [HttpPost("{id:guid}/presences")]
    public async Task<ActionResult<PresenceResponse>> AddPresence(
        Guid id,
        [FromBody] AddPresenceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _addPresenceValidator.ValidateAsync(request, cancellationToken);
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
            var presence = await _addPresenceHandler.HandleAsync(id, callerId, request, cancellationToken);
            var response = ToPresenceResponse(presence);
            return CreatedAtAction(nameof(GetPresences), new { id }, response);
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MatchAccessDeniedException)
        {
            return Forbid();
        }
        catch (PresenceWindowNotOpenException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (PresenceAlreadyExistsException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (DbUpdateException)
        {
            return Conflict(new ProblemDetails { Detail = "Player is already on the presence list." });
        }
    }

    /// <summary>PUT /api/v1/matches/{id}/presences/me</summary>
    [HttpPut("{id:guid}/presences/me")]
    public async Task<ActionResult<PresenceResponse>> UpdateMyPresence(
        Guid id,
        [FromBody] UpdateMyPresenceRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _updateMyPresenceValidator.ValidateAsync(request, cancellationToken);
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
            var presence = await _updateMyPresenceHandler.HandleAsync(id, callerId, request, cancellationToken);
            return Ok(ToPresenceResponse(presence));
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (PresenceNotFoundException ex)
        {
            return NotFound(new ProblemDetails { Detail = ex.Message });
        }
        catch (PresenceWindowNotOpenException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (SlotLimitReachedException ex) when (ex.AlreadyOnWaitingList)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (SlotLimitReachedException ex)
        {
            return Conflict(new ProblemDetails
            {
                Detail = ex.Message,
                Extensions = { ["waitingListPosition"] = ex.WaitingListPosition },
            });
        }
    }

    /// <summary>GET /api/v1/matches/{id}/presences</summary>
    [HttpGet("{id:guid}/presences")]
    public async Task<ActionResult<PresenceListResponse>> GetPresences(
        Guid id,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _getPresenceListHandler.HandleAsync(id, cancellationToken);
            return Ok(response);
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>DELETE /api/v1/matches/{id}/presences/{playerId}</summary>
    [HttpDelete("{id:guid}/presences/{playerId:guid}")]
    public async Task<IActionResult> RemovePresence(
        Guid id,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            await _removePresenceHandler.HandleAsync(id, callerId, playerId, cancellationToken);
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
        catch (InvalidMatchStatusTransitionException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (PresenceNotFoundException)
        {
            return NotFound();
        }
    }

    // --- Private helpers ---

    private bool TryGetCallerId(out Guid callerId)
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out callerId);
    }

    private static PresenceResponse ToPresenceResponse(MatchPresence p) =>
        new(
            Id: p.Id,
            MatchId: p.MatchId,
            PlayerId: p.PlayerId,
            PlayerType: p.PlayerType.ToString(),
            Status: p.Status.ToString(),
            ConfirmedAt: p.ConfirmedAt,
            CreatedAt: p.CreatedAt,
            UpdatedAt: p.UpdatedAt);

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
