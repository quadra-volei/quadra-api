namespace Quadra.Modules.Auth.Authentication;

/// <summary>
/// Standardized JSON body returned whenever the JWT bearer middleware challenges a request.
/// Keeping the shape stable lets API clients render a single error contract.
/// </summary>
public sealed record UnauthorizedErrorResponse(string Error, string Message);
