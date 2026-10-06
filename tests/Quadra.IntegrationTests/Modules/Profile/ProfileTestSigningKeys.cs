using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>
/// Generates a single in-process RSA key-pair for the Profile (F2.1) integration test suite.
/// Kept separate from the other suites' test keys to avoid cross-suite coupling.
/// </summary>
internal static class ProfileTestSigningKeys
{
    public const string KeyId = "profile-test-signing-key";

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
