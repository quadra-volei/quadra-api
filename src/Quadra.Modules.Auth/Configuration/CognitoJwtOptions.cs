namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// Strongly-typed options bound to the <c>Auth:Cognito</c> configuration section.
/// </summary>
public sealed class CognitoJwtOptions
{
    public const string SectionName = "Auth:Cognito";

    /// <summary>
    /// AWS Cognito User Pool identifier (e.g. <c>us-east-1_ABCdEfGhI</c>).
    /// </summary>
    public string UserPoolId { get; set; } = string.Empty;

    /// <summary>
    /// AWS region where the User Pool lives (e.g. <c>us-east-1</c>).
    /// </summary>
    public string Region { get; set; } = string.Empty;

    /// <summary>
    /// Cognito App Client ID. Validated against the <c>client_id</c> claim on access tokens
    /// (and against the <c>aud</c> claim on ID tokens, indirectly through the bearer middleware).
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// Allowed clock drift in seconds when validating <c>exp</c>/<c>nbf</c>. Defaults to 30.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;

    /// <summary>
    /// Computed authority URL used by the JWT bearer middleware for OIDC discovery + JWKS retrieval.
    /// </summary>
    public string Authority => $"https://cognito-idp.{Region}.amazonaws.com/{UserPoolId}";
}
