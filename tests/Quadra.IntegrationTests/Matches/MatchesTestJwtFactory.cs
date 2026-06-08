using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Quadra.IntegrationTests.Matches;

/// <summary>
/// Issues JWTs signed with <see cref="MatchesTestSigningKeys.PrivateKey"/> for use in
/// Matches integration tests. The <c>sub</c> claim is used by the controller as OrganizerId.
/// </summary>
internal static class MatchesTestJwtFactory
{
    public static string CreateAccessToken(
        string issuer,
        string clientId,
        string subject,
        DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("client_id", clientId),
            new("token_use", "access"),
        };

        var credentials = new SigningCredentials(
            MatchesTestSigningKeys.PrivateKey,
            SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: null,
            claims: claims,
            notBefore: now.AddMinutes(-1),
            expires: expires ?? now.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
