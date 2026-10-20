using FluentAssertions;
using Microsoft.Extensions.Hosting;
using NSubstitute;
using Quadra.Modules.Auth.Configuration;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for the fail-fast startup validators of <c>Auth:Jwt</c> and
/// <c>Auth:PhoneVerification</c>.
/// </summary>
public sealed class AuthOptionsValidatorTests
{
    private static IHostEnvironment Environment(string name)
    {
        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(name);
        return environment;
    }

    // ---------------- Auth:Jwt ----------------

    /// <summary>
    /// Covers FA.1: a complete configuration passes.
    /// </summary>
    [Fact]
    public void Jwt_options_with_all_values_pass()
    {
        var result = new JwtOptionsValidator(Environment("Production"))
            .Validate(null, AuthTestSupport.JwtOptions());

        result.Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// Covers FA.1: a missing or short signing key fails startup.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("too-short")]
    public void Jwt_options_with_missing_or_short_key_fail(string signingKey)
    {
        var options = AuthTestSupport.JwtOptions();
        options.SigningKey = signingKey;

        var result = new JwtOptionsValidator(Environment("Development")).Validate(null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Contain("Auth:Jwt:SigningKey");
    }

    /// <summary>
    /// Covers the guard against shipping the committed development key: accepted in Development
    /// and Testing, rejected everywhere else.
    /// </summary>
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Staging", false)]
    [InlineData("Production", false)]
    public void Development_key_is_only_accepted_in_development_and_testing(string environment, bool accepted)
    {
        var options = AuthTestSupport.JwtOptions();
        options.SigningKey = JwtOptions.DevelopmentKeyPrefix + "0123456789-0123456789-0123456789";

        var result = new JwtOptionsValidator(Environment(environment)).Validate(null, options);

        result.Succeeded.Should().Be(accepted);
    }

    /// <summary>
    /// Covers FA.1: non-positive lifetimes and blank issuer/audience fail.
    /// </summary>
    [Fact]
    public void Jwt_options_with_invalid_lifetimes_and_names_fail()
    {
        var options = AuthTestSupport.JwtOptions();
        options.Issuer = " ";
        options.Audience = "";
        options.AccessTokenMinutes = 0;
        options.RefreshTokenDays = -1;
        options.ClockSkewSeconds = -1;

        var result = new JwtOptionsValidator(Environment("Development")).Validate(null, options);

        result.Failures.Should().HaveCount(5);
    }

    // ---------------- Auth:PhoneVerification ----------------

    /// <summary>
    /// Covers FA.3: the fake provider is refused in Production and allowed elsewhere.
    /// </summary>
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Testing", true)]
    [InlineData("Production", false)]
    public void Fake_provider_is_refused_in_production(string environment, bool accepted)
    {
        var options = new PhoneVerificationOptions { Provider = "fake" };

        var result = new PhoneVerificationOptionsValidator(Environment(environment)).Validate(null, options);

        result.Succeeded.Should().Be(accepted);
    }

    /// <summary>
    /// Covers FA.3: Twilio requires AccountSid, AuthToken and VerifyServiceSid.
    /// </summary>
    [Fact]
    public void Twilio_provider_requires_all_credentials()
    {
        var validator = new PhoneVerificationOptionsValidator(Environment("Production"));
        var options = new PhoneVerificationOptions
        {
            Provider = PhoneVerificationOptions.ProviderTwilio,
            Twilio = new TwilioVerifyOptions { AccountSid = "AC123", AuthToken = "token" },
        };

        validator.Validate(null, options).Failed.Should().BeTrue();

        options.Twilio.VerifyServiceSid = "VA123";
        validator.Validate(null, options).Succeeded.Should().BeTrue();
    }

    /// <summary>
    /// Covers FA.3: an unknown provider name fails startup.
    /// </summary>
    [Fact]
    public void Unknown_provider_fails()
    {
        var options = new PhoneVerificationOptions { Provider = "Zenvia" };

        var result = new PhoneVerificationOptionsValidator(Environment("Development")).Validate(null, options);

        result.Failed.Should().BeTrue();
    }
}
