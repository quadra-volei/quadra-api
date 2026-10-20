using System.Buffers.Text;
using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Tokens;

/// <summary>
/// A freshly minted session: the response body for the client plus the refresh-token row the
/// caller must persist. The raw refresh token exists only inside <see cref="Tokens"/>.
/// </summary>
public sealed record IssuedSession(AuthTokensResponse Tokens, RefreshToken RefreshTokenRow);

/// <summary>
/// Mints a session for a user: a signed access token plus an opaque, random refresh token of
/// which only the SHA-256 hash is kept. Does not touch the database.
/// </summary>
public sealed class SessionFactory
{
    private const int RefreshTokenBytes = 32;

    private readonly IAccessTokenIssuer _accessTokenIssuer;
    private readonly IOptionsMonitor<JwtOptions> _options;

    public SessionFactory(IAccessTokenIssuer accessTokenIssuer, IOptionsMonitor<JwtOptions> options)
    {
        _accessTokenIssuer = accessTokenIssuer;
        _options = options;
    }

    /// <param name="familyId">The session being renewed (refresh); null starts a new one (login).</param>
    public IssuedSession Create(User user, string? deviceId, bool isNewUser, DateTimeOffset now, Guid? familyId = null)
    {
        ArgumentNullException.ThrowIfNull(user);

        var sessionId = familyId ?? Guid.NewGuid();
        var accessToken = _accessTokenIssuer.Issue(user, sessionId, now);
        var rawRefreshToken = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(RefreshTokenBytes));

        var refreshTokenRow = RefreshToken.Issue(
            Guid.NewGuid(),
            user.Id,
            RefreshTokenHasher.Hash(rawRefreshToken),
            deviceId,
            now,
            now.AddDays(_options.CurrentValue.RefreshTokenDays),
            sessionId);

        var tokens = new AuthTokensResponse(
            accessToken.Value,
            rawRefreshToken,
            "Bearer",
            accessToken.ExpiresIn,
            user.Id,
            isNewUser);

        return new IssuedSession(tokens, refreshTokenRow);
    }
}
