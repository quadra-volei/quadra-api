using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;

namespace Quadra.Modules.Auth.Oidc;

/// <summary>
/// Validates Google and Apple ID tokens by retrieving the provider JWKS through the standard
/// OpenID Connect configuration manager. The configuration is cached by the IdentityModel
/// library; we additionally apply a 15-minute <c>iat</c> skew check to limit replay.
/// </summary>
public sealed class GoogleAppleTokenValidator : IOidcTokenValidator
{
    private static readonly string[] GoogleValidIssuers =
    {
        "https://accounts.google.com",
        "accounts.google.com",
    };

    private static readonly TimeSpan MaxTokenAge = TimeSpan.FromMinutes(15);

    private readonly IOptionsMonitor<OidcProvidersOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ConcurrentDictionary<OidcProvider, ConfigurationManager<OpenIdConnectConfiguration>> _configurationManagers;
    private readonly JsonWebTokenHandler _tokenHandler;

    public GoogleAppleTokenValidator(
        IOptionsMonitor<OidcProvidersOptions> options,
        TimeProvider timeProvider)
    {
        _options = options;
        _timeProvider = timeProvider;
        _configurationManagers = new ConcurrentDictionary<OidcProvider, ConfigurationManager<OpenIdConnectConfiguration>>();
        _tokenHandler = new JsonWebTokenHandler { MapInboundClaims = false };
    }

    public async Task<OidcClaims> ValidateAsync(
        string idToken,
        OidcProvider provider,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);

        var providerOptions = GetProviderOptions(provider);
        var configManager = _configurationManagers.GetOrAdd(provider, _ => CreateConfigurationManager(providerOptions));

        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await configManager.GetConfigurationAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            throw new OidcTokenInvalidException(
                $"Failed to fetch OIDC discovery document for {provider}.", ex);
        }

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuers = provider == OidcProvider.Google
                ? GoogleValidIssuers
                : new[] { providerOptions.Issuer },
            ValidateAudience = true,
            ValidAudience = providerOptions.ClientId,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ClockSkew = TimeSpan.FromSeconds(30),
        };

        var validationResult = await _tokenHandler
            .ValidateTokenAsync(idToken, validationParameters)
            .ConfigureAwait(false);

        if (!validationResult.IsValid)
        {
            throw new OidcTokenInvalidException(
                $"ID token validation failed for {provider}.",
                validationResult.Exception);
        }

        if (validationResult.SecurityToken is JsonWebToken jwt)
        {
            if (jwt.TryGetPayloadValue<long>("iat", out var iatSeconds))
            {
                var iatOffset = DateTimeOffset.FromUnixTimeSeconds(iatSeconds);
                if (_timeProvider.GetUtcNow() - iatOffset > MaxTokenAge)
                {
                    throw new OidcTokenInvalidException(
                        $"ID token is older than the configured maximum age ({MaxTokenAge}).");
                }
            }
        }

        var claims = validationResult.ClaimsIdentity;
        var sub = claims.FindFirst("sub")?.Value;
        if (string.IsNullOrEmpty(sub))
        {
            throw new OidcTokenInvalidException("ID token is missing the required 'sub' claim.");
        }

        var email = claims.FindFirst("email")?.Value;
        var emailVerifiedClaim = claims.FindFirst("email_verified")?.Value;
        var emailVerified = string.Equals(emailVerifiedClaim, "true", StringComparison.OrdinalIgnoreCase);

        return new OidcClaims(sub, email, emailVerified);
    }

    private OidcProviderOptions GetProviderOptions(OidcProvider provider)
    {
        var current = _options.CurrentValue;
        return provider switch
        {
            OidcProvider.Google => current.Google,
            OidcProvider.Apple => current.Apple,
            _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null),
        };
    }

    private static ConfigurationManager<OpenIdConnectConfiguration> CreateConfigurationManager(
        OidcProviderOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(options.JwksUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Issuer);

        return new ConfigurationManager<OpenIdConnectConfiguration>(
            options.JwksUri,
            new OpenIdConnectConfigurationRetriever())
        {
            AutomaticRefreshInterval = TimeSpan.FromHours(1),
            RefreshInterval = TimeSpan.FromMinutes(5),
        };
    }
}
