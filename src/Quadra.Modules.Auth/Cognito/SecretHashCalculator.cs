using System.Security.Cryptography;
using System.Text;

namespace Quadra.Modules.Auth.Cognito;

/// <summary>
/// Computes the <c>SecretHash</c> required by Cognito App Clients that have a secret.
/// Algorithm: HMAC-SHA256(secret, username + appClientId) → base64.
/// </summary>
public static class SecretHashCalculator
{
    public static string Compute(string username, string appClientId, string appClientSecret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(appClientId);
        ArgumentException.ThrowIfNullOrWhiteSpace(appClientSecret);

        var key = Encoding.UTF8.GetBytes(appClientSecret);
        var message = Encoding.UTF8.GetBytes(username + appClientId);

        using var hmac = new HMACSHA256(key);
        var hash = hmac.ComputeHash(message);
        return Convert.ToBase64String(hash);
    }
}
