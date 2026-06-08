using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quadra.Modules.Auth.Configuration;

namespace Quadra.Modules.Auth.Authentication;

/// <summary>
/// <see cref="JwtBearerEvents"/> subclass that wires Cognito-specific concerns into the bearer middleware:
/// <list type="bullet">
///   <item>Reads <c>access_token</c> from the query string for SignalR <c>/hubs/*</c> WebSocket handshakes.</item>
///   <item>Validates the <c>client_id</c> claim against the configured <see cref="CognitoJwtOptions.Audience"/>
///         (Cognito access tokens do not carry <c>aud</c>).</item>
///   <item>Replaces the default empty challenge body with the standardized <see cref="UnauthorizedErrorResponse"/> JSON.</item>
///   <item>Logs authentication failures through Serilog using structured properties.</item>
/// </list>
/// </summary>
public sealed class CognitoJwtBearerEventsHandler : JwtBearerEvents
{
    private const string HubsPathPrefix = "/hubs";
    private const string AccessTokenQueryKey = "access_token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<CognitoJwtBearerEventsHandler> _logger;
    private readonly IOptionsMonitor<CognitoJwtOptions> _options;

    public CognitoJwtBearerEventsHandler(
        ILogger<CognitoJwtBearerEventsHandler> logger,
        IOptionsMonitor<CognitoJwtOptions> options)
    {
        _logger = logger;
        _options = options;

        OnMessageReceived = HandleMessageReceivedAsync;
        OnTokenValidated = HandleTokenValidatedAsync;
        OnAuthenticationFailed = HandleAuthenticationFailedAsync;
        OnChallenge = HandleChallengeAsync;
    }

    private static Task HandleMessageReceivedAsync(MessageReceivedContext context)
    {
        var path = context.HttpContext.Request.Path;
        if (path.HasValue && path.StartsWithSegments(HubsPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var accessToken = context.Request.Query[AccessTokenQueryKey].ToString();
            if (!string.IsNullOrEmpty(accessToken))
            {
                context.Token = accessToken;
            }
        }

        return Task.CompletedTask;
    }

    private Task HandleTokenValidatedAsync(TokenValidatedContext context)
    {
        var expectedClientId = _options.CurrentValue.Audience;
        var principal = context.Principal;

        if (principal is null)
        {
            context.Fail("Token principal was not populated.");
            return Task.CompletedTask;
        }

        var clientId = principal.FindFirstValue("client_id");
        var audience = principal.FindFirstValue("aud");

        // Access tokens carry client_id, ID tokens carry aud. Either must match the configured audience.
        var matchesClientId = !string.IsNullOrEmpty(clientId) &&
            string.Equals(clientId, expectedClientId, StringComparison.Ordinal);
        var matchesAudience = !string.IsNullOrEmpty(audience) &&
            string.Equals(audience, expectedClientId, StringComparison.Ordinal);

        if (!matchesClientId && !matchesAudience)
        {
            context.Fail("Token audience/client_id does not match the configured Cognito App Client.");
        }

        return Task.CompletedTask;
    }

    private Task HandleAuthenticationFailedAsync(AuthenticationFailedContext context)
    {
        _logger.LogWarning(
            context.Exception,
            "JWT authentication failed for {Method} {Path}",
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path.Value);

        return Task.CompletedTask;
    }

    private static async Task HandleChallengeAsync(JwtBearerChallengeContext context)
    {
        // Suppress the default empty body so we can write our own JSON contract.
        context.HandleResponse();

        var response = context.Response;
        response.StatusCode = StatusCodes.Status401Unauthorized;
        response.ContentType = "application/json; charset=utf-8";
        response.Headers["WWW-Authenticate"] = "Bearer error=\"invalid_token\"";

        var body = new UnauthorizedErrorResponse(
            Error: "unauthorized",
            Message: "A valid bearer token is required.");

        var cancellationToken = context.HttpContext.RequestAborted;
        await JsonSerializer.SerializeAsync(response.Body, body, JsonOptions, cancellationToken)
            ;
    }
}
