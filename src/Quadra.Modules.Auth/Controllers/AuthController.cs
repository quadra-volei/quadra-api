using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Validation;

namespace Quadra.Modules.Auth.Controllers;

/// <summary>
/// HTTP surface of the Auth module. Exposes the FA.2 signup endpoint and the FA.3 login flows
/// (SMS OTP, Google, Apple) plus refresh. Thin: actions only validate, invoke a handler, and map
/// exceptions to status codes.
/// </summary>
[ApiController]
[Route("api/v1/auth")]
public sealed class AuthController : ControllerBase
{
    private readonly IValidator<SignupRequest> _validator;
    private readonly SignupHandler _signupHandler;
    private readonly IValidator<SmsOtpLoginRequest> _smsOtpLoginValidator;
    private readonly IValidator<OidcLoginRequest> _oidcLoginValidator;
    private readonly IValidator<RefreshRequest> _refreshValidator;
    private readonly SmsOtpLoginHandler _smsOtpLoginHandler;
    private readonly OidcLoginHandler _oidcLoginHandler;
    private readonly RefreshHandler _refreshHandler;

    public AuthController(
        IValidator<SignupRequest> validator,
        SignupHandler signupHandler,
        IValidator<SmsOtpLoginRequest> smsOtpLoginValidator,
        IValidator<OidcLoginRequest> oidcLoginValidator,
        IValidator<RefreshRequest> refreshValidator,
        SmsOtpLoginHandler smsOtpLoginHandler,
        OidcLoginHandler oidcLoginHandler,
        RefreshHandler refreshHandler)
    {
        _validator = validator;
        _signupHandler = signupHandler;
        _smsOtpLoginValidator = smsOtpLoginValidator;
        _oidcLoginValidator = oidcLoginValidator;
        _refreshValidator = refreshValidator;
        _smsOtpLoginHandler = smsOtpLoginHandler;
        _oidcLoginHandler = oidcLoginHandler;
        _refreshHandler = refreshHandler;
    }

    /// <summary>
    /// POST /api/v1/auth/signup — provisions a new user via Cognito.
    /// </summary>
    [HttpPost("signup")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SignupResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Signup(
        [FromBody] SignupRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Request body is required.");
        }

        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var command = BuildCommand(request);

        try
        {
            var response = await _signupHandler.HandleAsync(command, cancellationToken);
            return CreatedAtAction(
                actionName: nameof(Signup),
                routeValues: new { id = response.UserId },
                value: response);
        }
        catch (PhoneAlreadyRegisteredException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
        catch (ExternalIdentityAlreadyLinkedException ex)
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: ex.Message);
        }
        catch (OidcTokenInvalidException ex)
        {
            return Problem(statusCode: StatusCodes.Status401Unauthorized, title: ex.Message);
        }
        catch (CognitoPolicyViolationException ex)
        {
            return Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: ex.Message);
        }
        catch (CognitoUnavailableException ex)
        {
            return Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
        }
    }

    /// <summary>
    /// POST /api/v1/auth/login/sms-otp — initiates or verifies an SMS OTP login.
    /// </summary>
    [HttpPost("login/sms-otp")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(SmsOtpInitiateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
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

        var validation = await _smsOtpLoginValidator.ValidateAsync(request, cancellationToken)
            ;
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
                    .InitiateAsync(request.PhoneNumber, cancellationToken)
                    ;
                return Ok(initiated);
            }

            var tokens = await _smsOtpLoginHandler
                .VerifyAsync(request.PhoneNumber, request.Session!, request.Code!, request.DeviceId, cancellationToken)
                ;
            return Ok(tokens);
        }
        catch (Exception ex) when (TryMapLoginException(ex, out var result))
        {
            return result;
        }
    }

    /// <summary>
    /// POST /api/v1/auth/login/google — exchanges a Google ID token for a Cognito session.
    /// </summary>
    [HttpPost("login/google")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public Task<IActionResult> LoginGoogle(
        [FromBody] OidcLoginRequest request,
        CancellationToken cancellationToken) =>
        HandleOidcLoginAsync(IdentityProvider.Google, request, cancellationToken);

    /// <summary>
    /// POST /api/v1/auth/login/apple — exchanges an Apple ID token for a Cognito session.
    /// </summary>
    [HttpPost("login/apple")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public Task<IActionResult> LoginApple(
        [FromBody] OidcLoginRequest request,
        CancellationToken cancellationToken) =>
        HandleOidcLoginAsync(IdentityProvider.Apple, request, cancellationToken);

    /// <summary>
    /// POST /api/v1/auth/refresh — renews a session from a refresh token.
    /// </summary>
    [HttpPost("refresh")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(AuthTokensResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> Refresh(
        [FromBody] RefreshRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Request body is required.");
        }

        var validation = await _refreshValidator.ValidateAsync(request, cancellationToken)
            ;
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        try
        {
            var tokens = await _refreshHandler
                .HandleAsync(request.RefreshToken, request.DeviceId, cancellationToken)
                ;
            return Ok(tokens);
        }
        catch (Exception ex) when (TryMapLoginException(ex, out var result))
        {
            return result;
        }
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

        var validation = await _oidcLoginValidator.ValidateAsync(request, cancellationToken)
            ;
        if (!validation.IsValid)
        {
            return ToValidationProblem(validation);
        }

        try
        {
            var tokens = await _oidcLoginHandler
                .HandleAsync(provider, request.IdToken, request.DeviceId, cancellationToken)
                ;
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
            case UserNotFoundForLoginException:
                result = Problem(statusCode: StatusCodes.Status404NotFound, title: ex.Message);
                return true;
            case CognitoAuthThrottledException:
                result = Problem(statusCode: StatusCodes.Status429TooManyRequests, title: ex.Message);
                return true;
            case CognitoUnavailableException:
                result = Problem(statusCode: StatusCodes.Status502BadGateway, title: ex.Message);
                return true;
            default:
                result = null!;
                return false;
        }
    }

    private static SignupCommand BuildCommand(SignupRequest request)
    {
        var normalized = request.Provider.Trim().ToLowerInvariant();
        return normalized switch
        {
            SignupRequestValidator.ProviderPhone =>
                new SignupCommand.Phone(request.Phone!.PhoneNumber),
            SignupRequestValidator.ProviderGoogle =>
                new SignupCommand.External(IdentityProvider.Google, request.Google!.IdToken),
            SignupRequestValidator.ProviderApple =>
                new SignupCommand.External(IdentityProvider.Apple, request.Apple!.IdToken),
            _ => throw new InvalidOperationException(
                $"Validator allowed unknown provider '{normalized}'."),
        };
    }
}
