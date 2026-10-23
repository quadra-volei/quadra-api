using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Shared.Contracts;

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
    private readonly UpdateMatchHandler _updateHandler;
    private readonly IValidator<UpdateMatchRequest> _updateValidator;
    private readonly UpdateMyPresenceHandler _updateMyPresenceHandler;
    private readonly GetPresenceListHandler _getPresenceListHandler;
    private readonly RemovePresenceHandler _removePresenceHandler;
    private readonly LeaveWaitingListHandler _leaveWaitingListHandler;
    private readonly GetMatchDetailHandler _getDetailHandler;
    private readonly ListMyMatchesHandler _listMineHandler;
    private readonly AddMatchGuestHandler _addGuestHandler;
    private readonly RemoveMatchGuestHandler _removeGuestHandler;
    private readonly IValidator<AddGuestRequest> _addGuestValidator;
    private readonly IValidator<CreateMatchRequest> _createValidator;
    private readonly IValidator<ListMatchesQuery> _listValidator;
    private readonly IValidator<UpdateMyPresenceRequest> _updateMyPresenceValidator;
    private readonly IMatchAccessReader _access;

    public MatchesController(
        CreateMatchHandler createHandler,
        GetMatchHandler getHandler,
        ListMatchesHandler listHandler,
        CancelMatchHandler cancelHandler,
        UpdateMatchHandler updateHandler,
        IValidator<UpdateMatchRequest> updateValidator,
        UpdateMyPresenceHandler updateMyPresenceHandler,
        GetPresenceListHandler getPresenceListHandler,
        RemovePresenceHandler removePresenceHandler,
        LeaveWaitingListHandler leaveWaitingListHandler,
        GetMatchDetailHandler getDetailHandler,
        ListMyMatchesHandler listMineHandler,
        AddMatchGuestHandler addGuestHandler,
        RemoveMatchGuestHandler removeGuestHandler,
        IValidator<AddGuestRequest> addGuestValidator,
        IValidator<CreateMatchRequest> createValidator,
        IValidator<ListMatchesQuery> listValidator,
        IValidator<UpdateMyPresenceRequest> updateMyPresenceValidator,
        IMatchAccessReader access)
    {
        _createHandler = createHandler;
        _getHandler = getHandler;
        _listHandler = listHandler;
        _cancelHandler = cancelHandler;
        _updateHandler = updateHandler;
        _updateValidator = updateValidator;
        _updateMyPresenceHandler = updateMyPresenceHandler;
        _getPresenceListHandler = getPresenceListHandler;
        _removePresenceHandler = removePresenceHandler;
        _leaveWaitingListHandler = leaveWaitingListHandler;
        _getDetailHandler = getDetailHandler;
        _listMineHandler = listMineHandler;
        _addGuestHandler = addGuestHandler;
        _removeGuestHandler = removeGuestHandler;
        _addGuestValidator = addGuestValidator;
        _createValidator = createValidator;
        _listValidator = listValidator;
        _updateMyPresenceValidator = updateMyPresenceValidator;
        _access = access;
    }

    /// <summary>POST /api/v1/matches</summary>
    [HttpPost]
    public async Task<ActionResult<MatchResponse>> Create(
        [FromBody] CreateMatchRequest request,
        [FromServices] IProfilePhotoStorage photoStorage,
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

        // ponytail: the cover reuses the profile-photo upload URL (as post photos do), so its key
        // lives under profiles/{userId}/. Give covers their own prefix if that ever matters.
        if (!string.IsNullOrWhiteSpace(request.CoverObjectKey)
            && (!request.CoverObjectKey.StartsWith($"profiles/{callerId}/", StringComparison.Ordinal)
                || !await photoStorage.IsWithinLimitsAsync(request.CoverObjectKey, cancellationToken)))
        {
            ModelState.AddModelError(nameof(request.CoverObjectKey), "The cover must be an image the caller uploaded, up to 10 MB, that passes the content check.");
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
            WindowClosesAt: request.WindowClosesAt,
            Format: request.Format,
            Level: request.Level,
            DurationMinutes: request.DurationMinutes,
            GamePoints: request.GamePoints,
            FreeTeams: request.FreeTeams,
            Visibility: request.Visibility,
            InviteMode: request.InviteMode,
            PriceMonthly: request.PriceMonthly,
            RecurrenceDays: request.RecurrenceDays,
            ConfirmationOpensHoursBefore: request.ConfirmationOpensHoursBefore,
            CoverObjectKey: string.IsNullOrWhiteSpace(request.CoverObjectKey) ? null : request.CoverObjectKey,
            PlaceId: request.PlaceId,
            ArenaId: request.ArenaId);

        try
        {
            var match = await _createHandler.HandleAsync(command, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = match.Id }, ToResponse(match));
        }
        catch (MatchArenaUnavailableException ex)
        {
            ModelState.AddModelError(nameof(request.ArenaId), ex.Message);
            return ValidationProblem(ModelState);
        }
    }

    /// <summary>GET /api/v1/matches/{id}</summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MatchResponse>> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(id, cancellationToken))
        {
            return NotFound();
        }

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
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _listValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var (items, totalCount) = await _listHandler.HandleAsync(query, callerId, cancellationToken);

        var response = new PagedMatchesResponse(
            Items: items.Select(ToResponse).ToList(),
            Page: query.Page,
            PageSize: query.PageSize,
            TotalCount: totalCount);

        return Ok(response);
    }

    /// <summary>
    /// PUT /api/v1/matches/{id} — the organizer edits the match until its game starts.
    /// </summary>
    [HttpPut("{id:guid}")]
    public async Task<ActionResult<MatchResponse>> Update(
        Guid id,
        [FromBody] UpdateMatchRequest request,
        [FromServices] IProfilePhotoStorage photoStorage,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        // A match the caller may not see answers 404 here too, so a write does not reveal it.
        if (!await _access.CanViewAsync(id, callerId, cancellationToken))
        {
            return NotFound();
        }

        var validation = await _updateValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        if (!string.IsNullOrEmpty(request.CoverObjectKey)
            && (!request.CoverObjectKey.StartsWith($"profiles/{callerId}/", StringComparison.Ordinal)
                || !await photoStorage.IsWithinLimitsAsync(request.CoverObjectKey, cancellationToken)))
        {
            ModelState.AddModelError(nameof(request.CoverObjectKey), "The cover must be an image the caller uploaded, up to 10 MB, that passes the content check.");
            return ValidationProblem(ModelState);
        }

        try
        {
            var match = await _updateHandler.HandleAsync(id, callerId, request, cancellationToken);
            return Ok(ToResponse(match));
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
        catch (MatchSlotsBelowConfirmedException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
        catch (MatchArenaUnavailableException ex)
        {
            ModelState.AddModelError(nameof(request.ArenaId), ex.Message);
            return ValidationProblem(ModelState);
        }
    }

    /// <summary>
    /// DELETE /api/v1/matches/{id} — the organizer cancels the match (kept as Cancelled) until
    /// its game starts. A recurring game schedules the group's next one unless
    /// <c>?endGroup=true</c>, which ends the group.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Cancel(
        Guid id,
        [FromQuery] bool endGroup,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        if (!await _access.CanViewAsync(id, callerId, cancellationToken))
        {
            return NotFound();
        }

        try
        {
            await _cancelHandler.HandleAsync(id, callerId, endGroup, cancellationToken);
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
        catch (MatchInviteRequiredException ex)
        {
            return Problem(statusCode: StatusCodes.Status403Forbidden, detail: ex.Message);
        }
        catch (PresenceAlreadyExistsException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
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

    /// <summary>DELETE /api/v1/matches/{id}/waiting-list/me — the caller leaves the waiting list.</summary>
    [HttpDelete("{id:guid}/waiting-list/me")]
    public async Task<IActionResult> LeaveWaitingList(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        // Only the caller's own entry is touched, so nothing about the match leaks: 404 either way.
        return await _leaveWaitingListHandler.HandleAsync(id, callerId, cancellationToken)
            ? NoContent()
            : NotFound();
    }

    /// <summary>GET /api/v1/matches/{id}/presences</summary>
    [HttpGet("{id:guid}/presences")]
    public async Task<ActionResult<PresenceListResponse>> GetPresences(
        Guid id,
        CancellationToken cancellationToken)
    {
        if (!await CanViewAsync(id, cancellationToken))
        {
            return NotFound();
        }

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

    /// <summary>
    /// GET /api/v1/matches/mine — the caller's upcoming matches (organized, joined or waiting).
    /// </summary>
    [HttpGet("mine")]
    public async Task<ActionResult<MyMatchesResponse>> ListMine(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return Ok(await _listMineHandler.HandleAsync(callerId, cancellationToken));
    }

    /// <summary>
    /// GET /api/v1/matches/{id}/detail — the match with its organizer, roster, guests, waiting
    /// list and the caller's own standing.
    /// </summary>
    [HttpGet("{id:guid}/detail")]
    public async Task<ActionResult<MatchDetailResponse>> GetDetail(
        Guid id,
        [FromQuery] string? inviteCode,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        // An invite link carries the code: its holder sees the private match before joining.
        if (!await _access.CanViewAsync(id, callerId, cancellationToken)
            && (string.IsNullOrWhiteSpace(inviteCode) || inviteCode.Length > 16
                || !await _access.IsValidInviteAsync(id, inviteCode, cancellationToken)))
        {
            return NotFound();
        }

        try
        {
            return Ok(await _getDetailHandler.HandleAsync(id, callerId, cancellationToken));
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// POST /api/v1/matches/{id}/guests — the organizer fills a slot with a player who has no
    /// account.
    /// </summary>
    [HttpPost("{id:guid}/guests")]
    public async Task<ActionResult<MatchGuestResponse>> AddGuest(
        Guid id,
        [FromBody] AddGuestRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _addGuestValidator.ValidateAsync(request, cancellationToken);
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
            var guest = await _addGuestHandler.HandleAsync(id, callerId, request, cancellationToken);
            return StatusCode(
                StatusCodes.Status201Created,
                new MatchGuestResponse(guest.Id, guest.Name, guest.Position, guest.CreatedAt));
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
        catch (MatchFullException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    /// <summary>
    /// DELETE /api/v1/matches/{id}/guests/{guestId} — the organizer removes a guest.
    /// </summary>
    [HttpDelete("{id:guid}/guests/{guestId:guid}")]
    public async Task<IActionResult> RemoveGuest(
        Guid id,
        Guid guestId,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            await _removeGuestHandler.HandleAsync(id, callerId, guestId, cancellationToken);
            return NoContent();
        }
        catch (MatchNotFoundException)
        {
            return NotFound();
        }
        catch (MatchGuestNotFoundException)
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
    }

    // --- Private helpers ---

    /// <summary>CanViewMatch for the caller; a denied caller gets 404, as for a missing match.</summary>
    private async Task<bool> CanViewAsync(Guid matchId, CancellationToken cancellationToken) =>
        TryGetCallerId(out var callerId) && await _access.CanViewAsync(matchId, callerId, cancellationToken);

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

    private static MatchResponse ToResponse(Match m) => MatchMapper.ToResponse(m);
}
