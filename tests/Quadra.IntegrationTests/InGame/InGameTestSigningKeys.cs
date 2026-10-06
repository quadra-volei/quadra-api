using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Quadra.IntegrationTests.InGame;

/// <summary>
/// Generates a single in-process RSA key-pair for the InGame integration test suite.
/// Kept separate from the Auth and Matches test keys to avoid cross-suite coupling.
/// </summary>
internal static class InGameTestSigningKeys
{
    public const string KeyId = "ingame-test-signing-key";

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
