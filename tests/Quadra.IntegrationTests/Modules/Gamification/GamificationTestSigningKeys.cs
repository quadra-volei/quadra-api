using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Quadra.IntegrationTests.Modules.Gamification;

/// <summary>
/// In-process RSA key-pair for the F2.3 group-ranking integration test suite. Kept separate from the
/// other suites' test keys to avoid cross-suite coupling.
/// </summary>
internal static class GamificationTestSigningKeys
{
    public const string KeyId = "gamification-test-signing-key";

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
