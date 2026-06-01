namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// Configuration required to provision Cognito users from the signup endpoint. Bound to
/// the existing <c>Auth:Cognito</c> section (which already carries <c>UserPoolId</c> and
/// <c>Region</c> for the JWT middleware from FA.1). Validated at startup.
/// </summary>
public sealed class CognitoSignupOptions
{
    public const string SectionName = "Auth:Cognito";

    /// <summary>
    /// Cognito App Client ID used for the <c>SignUp</c> SDK call.
    /// </summary>
    public string AppClientId { get; set; } = string.Empty;

    /// <summary>
    /// Optional Cognito App Client secret. When present, <c>SignUp</c> calls require a
    /// <c>SecretHash</c> computed by <see cref="Cognito.SecretHashCalculator"/>.
    /// </summary>
    public string? AppClientSecret { get; set; }

    /// <summary>
    /// Cognito User Pool identifier — same value used by the JWT middleware in FA.1.
    /// </summary>
    public string UserPoolId { get; set; } = string.Empty;

    /// <summary>
    /// AWS region for the User Pool — same value used by the JWT middleware in FA.1.
    /// </summary>
    public string Region { get; set; } = string.Empty;
}
