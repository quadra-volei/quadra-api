using FluentAssertions;
using Microsoft.Extensions.Options;
using NSubstitute;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.Oidc;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="GoogleAppleTokenValidator"/>. The validator is a thin wrapper around
/// <c>Microsoft.IdentityModel.Tokens</c>; we focus on argument guarding and the failure-translation
/// contract that the handler relies on (every invalid case must surface as <see cref="OidcTokenInvalidException"/>).
/// Network-bound paths against real Google/Apple JWKS are intentionally excluded from unit tests.
/// </summary>
public sealed class GoogleAppleTokenValidatorTests
{
    private static GoogleAppleTokenValidator CreateValidator()
    {
        var options = new OidcProvidersOptions
        {
            Google = new OidcProviderOptions
            {
                ClientId = "google-client",
                Issuer = "https://accounts.google.com",
                // 127.0.0.1:1 is reserved and unreachable; first JWKS fetch fails fast.
                JwksUri = "http://127.0.0.1:1/.well-known/openid-configuration",
            },
            Apple = new OidcProviderOptions
            {
                ClientId = "apple-client",
                Issuer = "https://appleid.apple.com",
                JwksUri = "http://127.0.0.1:1/.well-known/openid-configuration",
            },
        };

        var monitor = Substitute.For<IOptionsMonitor<OidcProvidersOptions>>();
        monitor.CurrentValue.Returns(options);

        return new GoogleAppleTokenValidator(monitor, TimeProvider.System);
    }

    /// <summary>
    /// Covers: defensive argument check — empty id token is rejected up-front.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Validate_with_blank_token_throws_argument_exception(string idToken)
    {
        var sut = CreateValidator();

        var act = async () => await sut.ValidateAsync(idToken, OidcProvider.Google, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    /// <summary>
    /// Covers: failure to fetch the JWKS / OIDC discovery document is translated to
    /// <see cref="OidcTokenInvalidException"/> (the contract the handler relies on to map to 401).
    /// </summary>
    [Fact]
    public async Task Validate_when_jwks_unreachable_throws_oidc_token_invalid()
    {
        var sut = CreateValidator();
        // Any structurally-plausible JWT (header.payload.signature). Since the JWKS endpoint is
        // unreachable, the validator should fail at the configuration-fetch step.
        const string idToken = "eyJhbGciOiJSUzI1NiJ9.eyJzdWIiOiIxIn0.signature";

        var act = async () => await sut.ValidateAsync(idToken, OidcProvider.Google, TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<OidcTokenInvalidException>();
    }
}
