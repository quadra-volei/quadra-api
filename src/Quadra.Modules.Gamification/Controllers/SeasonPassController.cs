using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Billing;
using Quadra.Modules.Gamification.Contracts;

namespace Quadra.Modules.Gamification.Controllers;

/// <summary>The caller's season pass (DECISIONS #98) and a player's card collection (#99).</summary>
[ApiController]
[Authorize]
public sealed class SeasonPassController : ControllerBase
{
    /// <summary>GET /api/v1/season-pass — 204 when no season is set up.</summary>
    [HttpGet("api/v1/season-pass")]
    public async Task<ActionResult<SeasonPassResponse>> Get(
        [FromServices] SeasonPassHandler handler,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var callerId))
        {
            return Unauthorized();
        }

        var pass = await handler.GetAsync(callerId, cancellationToken);
        return pass is null ? NoContent() : Ok(pass);
    }

    /// <summary>
    /// POST /api/v1/season-pass/purchases — records a pass bought on Google Play after Google confirms it
    /// (DECISIONS #109). 200 Active (also when the same purchase is sent again), 202 Pending (slow payment:
    /// send again once Play reports it paid), 400 not a real or valid purchase for this account, 404 not
    /// the product of a season on sale, 409 the purchase belongs to another account, 503 billing is off
    /// or Google could not be reached (retry).
    /// </summary>
    [HttpPost("api/v1/season-pass/purchases")]
    public async Task<IActionResult> Purchase(
        [FromBody] SeasonPassPurchaseRequest request,
        [FromServices] IValidator<SeasonPassPurchaseRequest> validator,
        [FromServices] SeasonPassPurchases purchases,
        [FromServices] ILogger<SeasonPassController> logger,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var callerId))
        {
            return Unauthorized();
        }

        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ValidationProblem(new ValidationProblemDetails(validation.ToDictionary()));
        }

        try
        {
            var result = await purchases.VerifyAsync(callerId, request, cancellationToken);
            return result.Status == "Pending" ? Accepted(result) : Ok(result);
        }
        catch (BillingNotConfiguredException)
        {
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Season pass purchases are not open yet.");
        }
        catch (GooglePlayUnavailableException ex)
        {
            // Google's answer says what is wrong (service account permission, product, package): Sentry gets it, the app does not.
            logger.LogError(ex, "Google Play could not confirm a season pass purchase.");
            return Problem(statusCode: StatusCodes.Status503ServiceUnavailable, title: "Google Play could not confirm the purchase now. Try again.");
        }
        catch (UnknownSeasonPassProductException)
        {
            return Problem(statusCode: StatusCodes.Status404NotFound, title: "This product is not the pass of a season on sale.");
        }
        catch (InvalidSeasonPassPurchaseException ex)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: ex.Message);
        }
        catch (SeasonPassPurchaseTakenException)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "This purchase belongs to another account.");
        }
    }

    /// <summary>
    /// GET /api/v1/card-collection?userId= — a player's season cards (DECISIONS #99), the caller's when
    /// no <paramref name="userId"/>. Any signed-in player may see another's, like their profile and ranking:
    /// only pieces won and whether each card is complete.
    /// </summary>
    [HttpGet("api/v1/card-collection")]
    public async Task<ActionResult<CardCollectionResponse>> GetCollection(
        [FromQuery] Guid? userId,
        [FromServices] SeasonPassHandler handler,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var callerId))
        {
            return Unauthorized();
        }

        return Ok(await handler.GetCollectionAsync(userId ?? callerId, cancellationToken));
    }
}
