using System.Text;
using FluentAssertions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Tokens;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="JwtAccessTokenIssuer"/> (FA.1/FA.3). The issued token must validate
/// with the same parameters the bearer middleware uses, and must not validate with another key.
/// </summary>
public sealed class JwtAccessTokenIssuerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static readonly User PhoneUser =
        User.CreateForPhone(Guid.NewGuid(), "+5511999990000", Now);

    private static JwtAccessTokenIssuer CreateSut() =>
        new(AuthTestSupport.Monitor(AuthTestSupport.JwtOptions()));

    private static TokenValidationParameters ValidationParameters(string signingKey) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = "quadra-test",
        ValidateAudience = true,
        ValidAudience = "quadra-test-client",
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
    };

    /// <summary>
    /// Covers FA.3: "the backend issues its own JWT" — signed HS256, with the user id as
    /// <c>sub</c>, the configured issuer/audience, and the configured lifetime.
    /// </summary>
    [Fact]
    public async Task Issued_token_validates_and_carries_the_user_id_as_subject()
    {
        var accessToken = CreateSut().Issue(PhoneUser, Now);

        accessToken.ExpiresIn.Should().Be(15 * 60);

        var result = await new JsonWebTokenHandler()
            .ValidateTokenAsync(accessToken.Value, ValidationParameters(AuthTestSupport.SigningKey));

        result.IsValid.Should().BeTrue();
        var jwt = (JsonWebToken)result.SecurityToken;
        jwt.Alg.Should().Be(SecurityAlgorithms.HmacSha256);
        jwt.Subject.Should().Be(PhoneUser.Id.ToString());
        jwt.GetClaim(JwtAccessTokenIssuer.ProviderClaim).Value.Should().Be("phone");
        jwt.Id.Should().NotBeNullOrWhiteSpace();
        jwt.ValidTo.Should().BeCloseTo(Now.AddMinutes(15).UtcDateTime, TimeSpan.FromSeconds(1));
    }

    /// <summary>
    /// Covers FA.1: a token signed with a different key is rejected.
    /// </summary>
    [Fact]
    public async Task Issued_token_does_not_validate_with_another_key()
    {
        var accessToken = CreateSut().Issue(PhoneUser, Now);

        var result = await new JsonWebTokenHandler().ValidateTokenAsync(
            accessToken.Value,
            ValidationParameters("another-signing-key-with-at-least-32-bytes!!!"));

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// Covers session minting: every session gets a distinct, high-entropy refresh token.
    /// </summary>
    [Fact]
    public void Session_factory_mints_distinct_refresh_tokens()
    {
        var factory = AuthTestSupport.SessionFactory();

        var first = factory.Create(PhoneUser, deviceId: null, isNewUser: false, Now);
        var second = factory.Create(PhoneUser, deviceId: null, isNewUser: false, Now);

        first.Tokens.RefreshToken.Should().NotBe(second.Tokens.RefreshToken);
        first.Tokens.RefreshToken.Length.Should().BeGreaterThanOrEqualTo(43);
        first.RefreshTokenRow.TokenHash.Should().NotBe(first.Tokens.RefreshToken);
    }
}
