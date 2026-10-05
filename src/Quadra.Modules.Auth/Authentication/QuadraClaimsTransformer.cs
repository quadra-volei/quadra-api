using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Quadra.Modules.Auth.Authentication;

/// <summary>
/// Normalizes the claims of a Quadra access token onto the canonical .NET claim types so
/// downstream authorization (e.g. <c>[Authorize(Roles = "...")]</c>) and identity helpers
/// (<c>User.FindFirstValue(ClaimTypes.NameIdentifier)</c>) work out of the box.
/// </summary>
public sealed class QuadraClaimsTransformer : IClaimsTransformation
{
    public const string SubjectClaim = "sub";
    public const string RoleClaim = "role";

    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
        {
            return Task.FromResult(principal);
        }

        // Clone so we never mutate the original principal more than once across the pipeline.
        var clone = principal.Clone();
        var cloneIdentity = (ClaimsIdentity)clone.Identity!;

        EnsureMappedClaim(cloneIdentity, SubjectClaim, ClaimTypes.NameIdentifier);
        EnsureMappedClaim(cloneIdentity, RoleClaim, ClaimTypes.Role);

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
