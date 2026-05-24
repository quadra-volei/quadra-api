using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Quadra.Modules.Auth.Authentication;

/// <summary>
/// Normalizes Cognito-issued claims onto the canonical .NET claim types so downstream
/// authorization (e.g. <c>[Authorize(Roles = "...")]</c>) and identity helpers
/// (<c>User.FindFirstValue(ClaimTypes.NameIdentifier)</c>) work out of the box.
/// </summary>
public sealed class CognitoClaimsTransformer : IClaimsTransformation
{
    private const string CognitoSubClaim = "sub";
    private const string CognitoRoleClaim = "custom:role";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        // Clone so we never mutate the original principal more than once across the pipeline.
        var clone = principal.Clone();
        var cloneIdentity = (ClaimsIdentity)clone.Identity!;

        EnsureMappedClaim(cloneIdentity, CognitoSubClaim, ClaimTypes.NameIdentifier);
        EnsureMappedClaim(cloneIdentity, CognitoRoleClaim, ClaimTypes.Role);

        return Task.FromResult(clone);
    }

    private static void EnsureMappedClaim(ClaimsIdentity identity, string sourceType, string targetType)
    {
        if (identity.HasClaim(c => c.Type == targetType))
        {
            return;
        }

        var source = identity.FindFirst(sourceType);
        if (source is null)
        {
            return;
        }

        identity.AddClaim(new Claim(targetType, source.Value, source.ValueType, source.Issuer));
    }
}
