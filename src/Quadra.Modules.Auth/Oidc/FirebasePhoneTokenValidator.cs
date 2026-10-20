using System.Text.Json;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;

namespace Quadra.Modules.Auth.Oidc;

/// <summary>Turns a Firebase ID token into the phone number Firebase verified by SMS.</summary>
public interface IFirebasePhoneTokenValidator
{
    /// <summary>The verified E.164 phone number. Throws <see cref="OidcTokenInvalidException"/> otherwise.</summary>
    Task<string> ValidateAsync(string idToken, CancellationToken cancellationToken);
}

/// <summary>
/// Firebase Phone Auth (DECISIONS #78): the app verifies the phone with Firebase and sends the
/// Firebase ID token. Accepted only when signed by Google's securetoken keys, issued for our
/// project (issuer and audience), not expired, at most 15 minutes old, signed in by phone and
/// carrying a <c>phone_number</c>.
/// </summary>
public sealed class FirebasePhoneTokenValidator : IFirebasePhoneTokenValidator
{
    private static readonly TimeSpan MaxTokenAge = TimeSpan.FromMinutes(15);

    private readonly IOptionsMonitor<PhoneVerificationOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly JsonWebTokenHandler _tokenHandler = new() { MapInboundClaims = false };
    private readonly IConfigurationManager<OpenIdConnectConfiguration>? _fixedKeys;
    private ConfigurationManager<OpenIdConnectConfiguration>? _discovery;
    private string? _discoveryProject;

    /// <param name="keys">Tests only: fixed signing keys instead of Google's discovery document.</param>
    public FirebasePhoneTokenValidator(
        IOptionsMonitor<PhoneVerificationOptions> options,
        TimeProvider timeProvider,
        IConfigurationManager<OpenIdConnectConfiguration>? keys = null)
    {
        _options = options;
        _timeProvider = timeProvider;
        _fixedKeys = keys;
    }

    public async Task<string> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idToken);

        var projectId = _options.CurrentValue.Firebase.ProjectId;
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new OidcTokenInvalidException("Firebase phone login is not configured.");
        }

        OpenIdConnectConfiguration configuration;
        try
        {
            configuration = await KeysFor(projectId).GetConfigurationAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new OidcTokenInvalidException("Failed to fetch the Firebase signing keys.", ex);
        }

        var result = await _tokenHandler.ValidateTokenAsync(idToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = $"https://securetoken.google.com/{projectId}",
            ValidateAudience = true,
            ValidAudience = projectId,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = configuration.SigningKeys,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ClockSkew = TimeSpan.FromSeconds(30),
        });
        if (!result.IsValid || result.SecurityToken is not JsonWebToken jwt)
        {
            throw new OidcTokenInvalidException("Firebase ID token validation failed.", result.Exception);
        }

        if (!jwt.TryGetPayloadValue<long>("iat", out var iat)
            || _timeProvider.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(iat) > MaxTokenAge)
        {
            throw new OidcTokenInvalidException($"Firebase ID token is older than {MaxTokenAge}.");
        }

        if (!jwt.TryGetPayloadValue<JsonElement>("firebase", out var firebase)
            || !firebase.TryGetProperty("sign_in_provider", out var provider)
            || provider.GetString() != "phone")
        {
            throw new OidcTokenInvalidException("The Firebase sign-in was not by phone.");
        }

        if (!jwt.TryGetPayloadValue<string>("phone_number", out var phone) || string.IsNullOrWhiteSpace(phone))
        {
            throw new OidcTokenInvalidException("The Firebase ID token carries no phone number.");
        }

        return phone;
    }

    private IConfigurationManager<OpenIdConnectConfiguration> KeysFor(string projectId)
    {
        if (_fixedKeys is not null)
        {
            return _fixedKeys;
        }

        // Google's discovery document for Firebase ID tokens; keys are cached and refreshed.
        if (_discovery is null || _discoveryProject != projectId)
        {
            _discovery = new ConfigurationManager<OpenIdConnectConfiguration>(
                $"https://securetoken.google.com/{projectId}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true });
            _discoveryProject = projectId;
        }

        return _discovery;
    }
}
