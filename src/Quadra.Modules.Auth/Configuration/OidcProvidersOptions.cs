namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// Configuration for the external OIDC providers (Google and Apple) used by the signup endpoint.
/// </summary>
public sealed class OidcProvidersOptions
{
    public const string SectionName = "Auth";

    public OidcProviderOptions Google { get; set; } = new();

    public OidcProviderOptions Apple { get; set; } = new();
}

/// <summary>
/// Per-provider OIDC values. <see cref="ClientId"/> becomes the expected <c>aud</c> claim;
/// <see cref="Issuer"/> the expected <c>iss</c>; <see cref="JwksUri"/> is queried for the
/// signature keys (cached with a 1-hour absolute expiration).
/// </summary>
public sealed class OidcProviderOptions
{
    public string ClientId { get; set; } = string.Empty;

    public string Issuer { get; set; } = string.Empty;

    public string JwksUri { get; set; } = string.Empty;
}
