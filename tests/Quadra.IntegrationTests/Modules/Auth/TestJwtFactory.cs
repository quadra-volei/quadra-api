using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Quadra.IntegrationTests.Modules.Auth;

/// <summary>
/// Issues JWTs signed with <see cref="TestSigningKeys.PrivateKey"/> that mimic
/// the structure of an AWS Cognito access token (iss, client_id, sub, email, custom:role).
/// </summary>
internal static class TestJwtFactory
{
    public static string CreateAccessToken(
        string issuer,
        string clientId,
        string subject = "11111111-1111-1111-1111-111111111111",
        string? email = "user@example.com",
        string? role = "player",
        string? cognitoUsername = "test-user",
        DateTime? notBefore = null,
        DateTime? expires = null,
        SigningCredentials? signingCredentials = null)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new("sub", subject),
            new("client_id", clientId),
            new("token_use", "access"),
            new("scope", "aws.cognito.signin.user.admin"),
        };

        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim("email", email));
        }

        if (!string.IsNullOrEmpty(role))
        {
            claims.Add(new Claim("custom:role", role));
        }

        if (!string.IsNullOrEmpty(cognitoUsername))
        {
            claims.Add(new Claim("cognito:username", cognitoUsername));
        }

        var credentials = signingCredentials ?? new SigningCredentials(
            TestSigningKeys.PrivateKey,
            SecurityAlgorithms.RsaSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: null,
            claims: claims,
            notBefore: notBefore ?? now.AddMinutes(-1),
            expires: expires ?? now.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
