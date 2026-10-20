namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// Strongly-typed options bound to the <c>Auth:Jwt</c> configuration section. The API issues and
/// validates its own HS256 access tokens with <see cref="SigningKey"/>.
/// </summary>
public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";

    /// <summary>
    /// Prefix reserved for the key committed in <c>appsettings.Development.json</c>. A key with
    /// this prefix is rejected outside the Development and Testing environments.
    /// </summary>
    public const string DevelopmentKeyPrefix = "dev-only-";

    /// <summary>
    /// Minimum signing key size in bytes (256 bits, the HS256 minimum).
    /// </summary>
    public const int MinSigningKeyBytes = 32;

    /// <summary>
    /// Value of the <c>iss</c> claim on issued tokens; also the only accepted issuer.
    /// </summary>
    public string Issuer { get; set; } = "quadra-api";

    /// <summary>
    /// Value of the <c>aud</c> claim on issued tokens; also the only accepted audience.
    /// </summary>
    public string Audience { get; set; } = "quadra-mobile";

    /// <summary>
    /// Symmetric signing secret (UTF-8). Must come from the environment / secret store in every
    /// deployed environment (<c>Auth__Jwt__SigningKey</c>).
    /// </summary>
    public string SigningKey { get; set; } = string.Empty;

    /// <summary>
    /// Access token lifetime in minutes. Kept short because access tokens cannot be revoked.
    /// </summary>
    public int AccessTokenMinutes { get; set; } = 15;

    /// <summary>
    /// Refresh token lifetime in days. Each refresh rotates the token and restarts this window.
    /// </summary>
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>
    /// Allowed clock drift in seconds when validating <c>exp</c>/<c>nbf</c>. Defaults to 30.
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 30;
}
