using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Tokens;

/// <summary>
/// <see cref="IAccessTokenIssuer"/> that signs HS256 JWTs with the key from <c>Auth:Jwt</c>.
/// The bearer middleware validates tokens with the same options (see <c>AddAuthModule</c>).
/// </summary>
public sealed class JwtAccessTokenIssuer : IAccessTokenIssuer
{
    public const string ProviderClaim = "provider";

    private readonly IOptionsMonitor<JwtOptions> _options;
    private readonly JsonWebTokenHandler _tokenHandler = new();

    public JwtAccessTokenIssuer(IOptionsMonitor<JwtOptions> options)
    {
        _options = options;
    }

    public AccessToken Issue(User user, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(user);

        var options = _options.CurrentValue;
        var lifetime = TimeSpan.FromMinutes(options.AccessTokenMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = now.Add(lifetime).UtcDateTime,
            Claims = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Jti] = Guid.NewGuid().ToString(),
                [ProviderClaim] = user.Provider.ToString().ToLowerInvariant(),
            },
            SigningCredentials = new SigningCredentials(
                CreateSigningKey(options),
                SecurityAlgorithms.HmacSha256),
        };

        return new AccessToken(_tokenHandler.CreateToken(descriptor), (int)lifetime.TotalSeconds);
    }

    /// <summary>
    /// Builds the symmetric key shared by token issuance and the bearer middleware.
    /// </summary>
    public static SymmetricSecurityKey CreateSigningKey(JwtOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey));
    }
}
