using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Quadra.IntegrationTests.Modules.Auth;

/// <summary>
/// Generates a single in-process RSA key-pair shared by the test JWT issuer (private key)
/// and the JWT bearer middleware (public key, served as a static OIDC configuration).
/// Avoids any real network round-trip to Cognito.
/// </summary>
internal static class TestSigningKeys
{
    public const string KeyId = "test-signing-key";

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
