using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Quadra.IntegrationTests.Matches;

/// <summary>
/// Generates a single in-process RSA key-pair for the Matches integration test suite.
/// Kept separate from the Auth test keys to avoid cross-suite coupling.
/// </summary>
internal static class MatchesTestSigningKeys
{
    public const string KeyId = "matches-test-signing-key";

    public static RSA Rsa { get; } = RSA.Create(2048);

    public static RsaSecurityKey PublicKey { get; } = new(Rsa.ExportParameters(includePrivateParameters: false))
    {
        KeyId = KeyId,
    };

    public static RsaSecurityKey PrivateKey { get; } = new(Rsa)
    {
        KeyId = KeyId,
    };
}
