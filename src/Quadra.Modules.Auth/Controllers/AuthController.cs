using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Persistence;

namespace Quadra.Modules.Auth.Controllers;

/// <summary>
/// HTTP surface of the Auth module: the FA.3 login flows (SMS OTP, Google, Apple), refresh,
/// logout and the current-user lookup. There is no signup endpoint — the first successful login
/// creates the account. Thin: actions only validate, invoke a handler, and map exceptions to
/// status codes.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IValidator<SmsOtpLoginRequest> _smsOtpLoginValidator;
    private readonly IValidator<OidcLoginRequest> _oidcLoginValidator;
    private readonly IValidator<RefreshRequest> _refreshValidator;
    private readonly IValidator<LogoutRequest> _logoutValidator;
    private readonly SmsOtpLoginHandler _smsOtpLoginHandler;
    private readonly OidcLoginHandler _oidcLoginHandler;
    private readonly RefreshHandler _refreshHandler;
    private readonly LogoutHandler _logoutHandler;
    private readonly IUserRepository _userRepository;

    public AuthController(
        IValidator<SmsOtpLoginRequest> smsOtpLoginValidator,
        IValidator<OidcLoginRequest> oidcLoginValidator,
        IValidator<RefreshRequest> refreshValidator,
        IValidator<LogoutRequest> logoutValidator,
        SmsOtpLoginHandler smsOtpLoginHandler,
        OidcLoginHandler oidcLoginHandler,
        RefreshHandler refreshHandler,
        LogoutHandler logoutHandler,
        IUserRepository userRepository)
    {
        _smsOtpLoginValidator = smsOtpLoginValidator;
        _oidcLoginValidator = oidcLoginValidator;
        _refreshValidator = refreshValidator;
        _logoutValidator = logoutValidator;
        _smsOtpLoginHandler = smsOtpLoginHandler;
        _oidcLoginHandler = oidcLoginHandler;
        _refreshHandler = refreshHandler;
        _logoutHandler = logoutHandler;
        _userRepository = userRepository;
    }

    /// <summary>
    /// POST /api/v1/auth/login/sms-otp — sends an SMS code (initiate) or checks it (verify).
    /// A valid code logs the phone number in, creating its account on first use.
    /// </summary>
    [HttpPost("login/sms-otp")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SmsOtpInitiateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> LoginSmsOtp(
        [FromBody] SmsOtpLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request body is required.");
        }

        var validation = await _smsOtpLoginValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        var step = request.Step.Trim().ToLowerInvariant();

        try
        {
            if (step == SmsOtpLoginHandler.StepInitiate)
            {
                var initiated = await _smsOtpLoginHandler
                    .InitiateAsync(request.PhoneNumber, cancellationToken);
                return Ok(initiated);
            }

            var tokens = await _smsOtpLoginHandler
                .VerifyAsync(request.PhoneNumber, request.Code!, request.DeviceId, cancellationToken);
            return Ok(tokens);
        }
        catch (Exception ex) when (TryMapLoginException(ex, out var result))
        {
            return result;
        }
    }

    /// <summary>
    /// POST /api/v1/auth/login/google — exchanges a Google ID token for a Quadra session.
    /// </summary>
    [HttpPost("login/google")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> LoginGoogle(
        [FromBody] OidcLoginRequest request,
        CancellationToken cancellationToken) =>
        HandleOidcLoginAsync(IdentityProvider.Google, request, cancellationToken);

    /// <summary>
    /// POST /api/v1/auth/login/apple — exchanges an Apple ID token for a Quadra session.
    /// </summary>
    [HttpPost("login/apple")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public Task<IActionResult> LoginApple(
        [FromBody] OidcLoginRequest request,
        CancellationToken cancellationToken) =>
        HandleOidcLoginAsync(IdentityProvider.Apple, request, cancellationToken);

    /// <summary>
    /// POST /api/v1/auth/refresh — renews a session from a refresh token, rotating it.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request body is required.");
        }

        var validation = await _refreshValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        try
        {
            var tokens = await _refreshHandler
                .HandleAsync(request.RefreshToken, request.DeviceId, cancellationToken);
            return Ok(tokens);
        }
        catch (Exception ex) when (TryMapLoginException(ex, out var result))
        {
            return result;
        }
    }

    /// <summary>
    /// POST /api/v1/auth/logout — revokes the refresh token of the session being ended.
    /// Always 204 for a well-formed request, whether or not the token was still valid.
    /// </summary>
    [HttpPost("logout")]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Logout(
        [FromBody] LogoutRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request body is required.");
        }

        var validation = await _logoutValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        await _logoutHandler.HandleAsync(request.RefreshToken, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// GET /api/v1/auth/me — the account behind the presented access token. Clients call it on
    /// startup to confirm a stored session is still good.
    /// </summary>
    [HttpGet("me")]
    [Authorize]
    [ProducesResponseType(typeof(CurrentUserResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Me(CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The token has no valid subject.");
        }

        var user = await _userRepository.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            // A still-valid token for an account that no longer exists.
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "The account no longer exists.");
        }

        return Ok(new CurrentUserResponse(
            user.Id,
            user.Provider.ToString().ToLowerInvariant(),
            user.PhoneNumber,
            user.Email));
    }

    private async Task<IActionResult> HandleOidcLoginAsync(
        IdentityProvider provider,
        OidcLoginRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request body is required.");
        }

        var validation = await _oidcLoginValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        try
        {
            var tokens = await _oidcLoginHandler
                .HandleAsync(provider, request.IdToken, request.DeviceId, cancellationToken);
            return Ok(tokens);
        }
        catch (Exception ex) when (TryMapLoginException(ex, out var result))
        {
            return result;
        }
    }

    private IActionResult ToValidationProblem(FluentValidation.Results.ValidationResult validation)
    {
        foreach (var failure in validation.Errors)
        {
            ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
        }

        return ValidationProblem(ModelState);
    }

    private bool TryMapLoginException(Exception ex, out IActionResult result)
    {
        switch (ex)
        {
            case OidcTokenInvalidException:
            case InvalidOtpException:
            case OtpChallengeExpiredException:
            case RefreshTokenRejectedException:
                result = Problem(statusCode: StatusCodes.Status401Unauthorized, title: ex.Message);
                return true;
            case PhoneNumberRejectedException:
                result = Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: ex.Message);
                return true;
            case PhoneVerificationThrottledException:
                result = Problem(statusCode: StatusCodes.Status429TooManyRequests, title: ex.Message);
                return true;
            case PhoneVerificationUnavailableException:
                result = Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
                return true;
            default:
                result = null!;
                return false;
        }
    }
}
