namespace Quadra.Modules.Auth.Oidc;

/// <summary>
/// External OIDC provider for which an ID token must be validated.
/// </summary>
public enum OidcProvider
{
    Google = 1,
    Apple = 2,
}

/// <summary>
/// Claims extracted from a successfully validated external ID token.
/// </summary>
public sealed record OidcClaims(
    string Subject,
    string? Email,
    bool EmailVerified);

/// <summary>
/// Validates Google and Apple ID tokens server-side. JWKS keys are cached.
/// </summary>
public interface IOidcTokenValidator
{
    Task<OidcClaims> ValidateAsync(
        string idToken,
        OidcProvider provider,
        CancellationToken cancellationToken);
}
