using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Quadra.IntegrationTests.Modules.Auth;

/// <summary>
/// Test-host settings for <c>Auth:Jwt</c> plus a factory that issues access tokens shaped like
/// the ones the API signs itself (HS256; iss, aud, sub, optional role). Tests can override any
/// part to produce a token the middleware must reject.
/// </summary>
internal static class TestJwtFactory
{
    public const string Issuer = "quadra-api-tests";
    public const string Audience = "quadra-mobile-tests";
    public const string SigningKey = "integration-test-signing-key-with-32-bytes-or-more";

    /// <summary>
    /// The <c>Auth</c> configuration every integration test host needs to pass startup
    /// validation: own-JWT settings, the fake phone verification, and a Google client ID.
    /// </summary>
    public static IEnumerable<KeyValuePair<string, string?>> AuthSettings() =>
    [
        new("Auth:Jwt:Issuer", Issuer),
        new("Auth:Jwt:Audience", Audience),
        new("Auth:Jwt:SigningKey", SigningKey),
        new("Auth:Jwt:ClockSkewSeconds", "30"),
        new("Auth:PhoneVerification:Provider", "Fake"),
        new("Auth:PhoneVerification:Fake:Code", FakeOtpCode),
        new("Auth:Google:ClientId", "test-google-client"),
    ];

    public const string FakeOtpCode = "123456";

    public static string CreateAccessToken(
        string subject = "11111111-1111-1111-1111-111111111111",
        string? role = null,
        string? email = null,
        string issuer = Issuer,
        string audience = Audience,
        string signingKey = SigningKey,
        DateTime? notBefore = null,
        DateTime? expires = null)
    {
        var now = DateTime.UtcNow;
        var claims = new List<Claim> { new("sub", subject) };

        if (!string.IsNullOrEmpty(role))
        {
            claims.Add(new Claim("role", role));
        }

        if (!string.IsNullOrEmpty(email))
        {
            claims.Add(new Claim("email", email));
        }

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: claims,
            notBefore: notBefore ?? now.AddMinutes(-1),
            expires: expires ?? now.AddMinutes(15),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
