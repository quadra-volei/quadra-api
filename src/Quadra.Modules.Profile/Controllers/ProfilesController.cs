using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Application;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Validation;

namespace Quadra.Modules.Profile.Controllers;

/// <summary>
/// Thin controller for player-profile endpoints. Business logic lives in the handlers; this class
/// only orchestrates validation, calls the handler and maps HTTP concerns.
/// </summary>
[ApiController]
[Route("api/v1/profiles")]
[Authorize]
public sealed class ProfilesController : ControllerBase
{
    private readonly GetProfileHandler _getProfileHandler;
    private readonly UpdateProfileHandler _updateProfileHandler;
    private readonly GetMatchHistoryHandler _getMatchHistoryHandler;
    private readonly CreatePhotoUploadUrlHandler _createPhotoUploadUrlHandler;
    private readonly GetPlayerCardHandler _getPlayerCardHandler;
    private readonly IValidator<UpdateProfileRequest> _updateValidator;
    private readonly IValidator<PhotoUploadUrlRequest> _photoUploadValidator;
    private readonly IValidator<MatchHistoryQuery> _matchHistoryValidator;

    public ProfilesController(
        GetProfileHandler getProfileHandler,
        UpdateProfileHandler updateProfileHandler,
        GetMatchHistoryHandler getMatchHistoryHandler,
        CreatePhotoUploadUrlHandler createPhotoUploadUrlHandler,
        GetPlayerCardHandler getPlayerCardHandler,
        IValidator<UpdateProfileRequest> updateValidator,
        IValidator<PhotoUploadUrlRequest> photoUploadValidator,
        IValidator<MatchHistoryQuery> matchHistoryValidator)
    {
        _getProfileHandler = getProfileHandler;
        _updateProfileHandler = updateProfileHandler;
        _getMatchHistoryHandler = getMatchHistoryHandler;
        _createPhotoUploadUrlHandler = createPhotoUploadUrlHandler;
        _getPlayerCardHandler = getPlayerCardHandler;
        _updateValidator = updateValidator;
        _photoUploadValidator = photoUploadValidator;
        _matchHistoryValidator = matchHistoryValidator;
    }

    /// <summary>GET /api/v1/profiles/me</summary>
    [HttpGet("me")]
    public async Task<ActionResult<PlayerProfileResponse>> GetMe(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        try
        {
            return Ok(await _getProfileHandler.HandleAsync(callerId, cancellationToken));
        }
        catch (ProfileNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>GET /api/v1/profiles/{userId}</summary>
    [HttpGet("{userId:guid}")]
    public async Task<ActionResult<PlayerProfileResponse>> GetById(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _getProfileHandler.HandleAsync(userId, cancellationToken));
        }
        catch (ProfileNotFoundException)
        {
            return NotFound();
        }
    }

    /// <summary>PUT /api/v1/profiles/me</summary>
    [HttpPut("me")]
    public async Task<ActionResult<PlayerProfileResponse>> UpdateMe(
        [FromBody] UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validationContext = new ValidationContext<UpdateProfileRequest>(request)
        {
            RootContextData = { [UpdateProfileRequestValidator.CallerIdKey] = callerId },
        };

        var validation = await _updateValidator.ValidateAsync(validationContext, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        try
        {
            return Ok(await _updateProfileHandler.HandleAsync(callerId, request, cancellationToken));
        }
        catch (ProfileNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidPositionException ex)
        {
            ModelState.AddModelError(nameof(UpdateProfileRequest.PrimaryPosition), ex.Message);
            return ValidationProblem(ModelState);
        }
    }

    /// <summary>POST /api/v1/profiles/me/photo/upload-url</summary>
    [HttpPost("me/photo/upload-url")]
    public async Task<ActionResult<PhotoUploadUrlResponse>> CreatePhotoUploadUrl(
        [FromBody] PhotoUploadUrlRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _photoUploadValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        try
        {
            var response = await _createPhotoUploadUrlHandler.HandleAsync(
                callerId, request.ContentType, cancellationToken);

            return StatusCode(StatusCodes.Status201Created, response);
        }
        catch (ProfilePhotoStorageUnavailableException)
        {
            // No object storage in this environment: photos are optional, everything else works.
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "Profile photos are not available in this environment.");
        }
    }

    /// <summary>GET /api/v1/profiles/me/match-history</summary>
    [HttpGet("me/match-history")]
    public async Task<ActionResult<PagedResponse<MatchHistoryEntryResponse>>> GetMyMatchHistory(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return await GetMatchHistoryAsync(callerId, page, pageSize, cancellationToken);
    }

    /// <summary>GET /api/v1/profiles/{userId}/match-history</summary>
    [HttpGet("{userId:guid}/match-history")]
    public async Task<ActionResult<PagedResponse<MatchHistoryEntryResponse>>> GetMatchHistoryById(
        Guid userId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        return await GetMatchHistoryAsync(userId, page, pageSize, cancellationToken);
    }

    /// <summary>GET /api/v1/profiles/me/card</summary>
    [HttpGet("me/card")]
    public async Task<ActionResult<PlayerCardResponse>> GetMyCard(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return await GetCardAsync(callerId, cancellationToken);
    }

    /// <summary>GET /api/v1/profiles/{userId}/card</summary>
    [HttpGet("{userId:guid}/card")]
    public async Task<ActionResult<PlayerCardResponse>> GetCardById(
        Guid userId,
        CancellationToken cancellationToken)
    {
        return await GetCardAsync(userId, cancellationToken);
    }

    // --- Private helpers ---

    private async Task<ActionResult<PlayerCardResponse>> GetCardAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await _getPlayerCardHandler.HandleAsync(userId, cancellationToken));
        }
        catch (ProfileNotFoundException)
        {
            return NotFound();
        }
        catch (CardNotGeneratedException ex)
        {
            return Conflict(new ProblemDetails { Detail = ex.Message });
        }
    }

    private async Task<ActionResult<PagedResponse<MatchHistoryEntryResponse>>> GetMatchHistoryAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var query = new MatchHistoryQuery(page, pageSize);
        var validation = await _matchHistoryValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationFailure(validation);
        }

        try
        {
            return Ok(await _getMatchHistoryHandler.HandleAsync(userId, page, pageSize, cancellationToken));
        }
        catch (ProfileNotFoundException)
        {
            return NotFound();
        }
    }

    private ActionResult ValidationFailure(FluentValidation.Results.ValidationResult validation)
    {
        foreach (var failure in validation.Errors)
        {
            ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
        }

        return ValidationProblem(ModelState);
    }

    private bool TryGetCallerId(out Guid callerId)
    {
        var value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(value, out callerId);
    }
}
