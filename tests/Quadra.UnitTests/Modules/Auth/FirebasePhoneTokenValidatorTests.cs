using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.Oidc;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>DECISIONS #78: only a fresh Firebase ID token of our project, signed in by phone, logs a phone in.</summary>
public sealed class FirebasePhoneTokenValidatorTests
{
    private const string Project = "quadra-volei-app";
    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "k1" };
    private static readonly RsaSecurityKey OtherKey = new(RSA.Create(2048)) { KeyId = "k1" };

    private static FirebasePhoneTokenValidator Sut(string projectId = Project)
    {
        var options = Substitute.For<IOptionsMonitor<PhoneVerificationOptions>>();
        options.CurrentValue.Returns(new PhoneVerificationOptions { Firebase = new FirebasePhoneOptions { ProjectId = projectId } });
        var config = new OpenIdConnectConfiguration();
        config.SigningKeys.Add(Key);
        return new FirebasePhoneTokenValidator(options, TimeProvider.System, new StaticConfigurationManager<OpenIdConnectConfiguration>(config));
    }

    private static string Token(
        string audience = Project,
        string signInProvider = "phone",
        string? phone = "+5511999990000",
        TimeSpan? age = null,
        SecurityKey? key = null)
    {
        var issuedAt = DateTime.UtcNow - (age ?? TimeSpan.FromMinutes(1));
        var claims = new Dictionary<string, object>
        {
            ["sub"] = "firebase-uid-1",
            ["firebase"] = new Dictionary<string, object> { ["sign_in_provider"] = signInProvider },
        };
        if (phone is not null)
        {
            claims["phone_number"] = phone;
        }

        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = $"https://securetoken.google.com/{Project}",
            Audience = audience,
            IssuedAt = issuedAt,
            NotBefore = issuedAt,
            Expires = DateTime.UtcNow.AddMinutes(30),
            Claims = claims,
            SigningCredentials = new SigningCredentials(key ?? Key, SecurityAlgorithms.RsaSha256),
        });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_fresh_phone_token_of_our_project_gives_the_phone() =>
        (await Sut().ValidateAsync(Token(), Ct)).Should().Be("+5511999990000");

    public static TheoryData<string> Refused() => new()
    {
        Token(audience: "another-project"),
        Token(signInProvider: "google.com"),
        Token(phone: null),
        Token(age: TimeSpan.FromMinutes(20)),
        Token(key: OtherKey),
        "not-a-jwt",
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task Anything_else_is_refused(string token) =>
        await FluentActions.Awaiting(() => Sut().ValidateAsync(token, Ct)).Should().ThrowAsync<OidcTokenInvalidException>();

    [Fact]
    public async Task Without_a_project_nothing_is_accepted() =>
        await FluentActions.Awaiting(() => Sut(projectId: "").ValidateAsync(Token(), Ct)).Should().ThrowAsync<OidcTokenInvalidException>();
}
