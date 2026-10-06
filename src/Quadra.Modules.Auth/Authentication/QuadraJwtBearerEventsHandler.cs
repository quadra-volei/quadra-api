using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Quadra.Modules.Auth.Authentication;

/// <summary>
/// <see cref="JwtBearerEvents"/> subclass that wires Quadra-specific concerns into the bearer middleware:
/// <list type="bullet">
///   <item>Reads <c>access_token</c> from the query string for SignalR <c>/hubs/*</c> WebSocket handshakes.</item>
///   <item>Replaces the default empty challenge body with the standardized <see cref="UnauthorizedErrorResponse"/> JSON.</item>
///   <item>Logs authentication failures through Serilog using structured properties.</item>
/// </list>
/// </summary>
public sealed class QuadraJwtBearerEventsHandler : JwtBearerEvents
{
    private const string HubsPathPrefix = "/hubs";
    private const string AccessTokenQueryKey = "access_token";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ILogger<QuadraJwtBearerEventsHandler> _logger;

    public QuadraJwtBearerEventsHandler(ILogger<QuadraJwtBearerEventsHandler> logger)
    {
        _logger = logger;

        OnMessageReceived = HandleMessageReceivedAsync;
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
        await JsonSerializer.SerializeAsync(response.Body, body, JsonOptions, cancellationToken);
    }
}
