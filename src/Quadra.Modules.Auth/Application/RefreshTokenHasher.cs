using System.Security.Cryptography;
using System.Text;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Computes the SHA-256 hex digest of a raw refresh token. Only the digest is persisted in
/// <c>refresh_tokens.token_hash</c>; the raw token is never stored or logged.
/// </summary>
public static class RefreshTokenHasher
{
    public static string Hash(string refreshToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);

        var bytes = Encoding.UTF8.GetBytes(refreshToken);
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(digest);
    }
}
