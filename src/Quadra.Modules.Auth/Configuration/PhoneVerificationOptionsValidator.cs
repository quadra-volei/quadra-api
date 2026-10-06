using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// Enforces the <c>Auth:PhoneVerification</c> settings at application startup: a known provider,
/// complete Twilio credentials when Twilio is selected, and no fake provider in Production.
/// </summary>
public sealed class PhoneVerificationOptionsValidator : IValidateOptions<PhoneVerificationOptions>
{
    private readonly IHostEnvironment _environment;

    public PhoneVerificationOptionsValidator(IHostEnvironment environment)
    {
        _environment = environment;
    }

    public ValidateOptionsResult Validate(string? name, PhoneVerificationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.UsesFake)
        {
            if (_environment.IsProduction())
            {
                return ValidateOptionsResult.Fail(
                    "Auth:PhoneVerification:Provider 'Fake' is not allowed in Production.");
            }

            return string.IsNullOrWhiteSpace(options.Fake.Code)
                ? ValidateOptionsResult.Fail("Auth:PhoneVerification:Fake:Code is required.")
                : ValidateOptionsResult.Success;
        }

        if (options.UsesTwilio)
        {
            var twilio = options.Twilio;
            return string.IsNullOrWhiteSpace(twilio.AccountSid)
                || string.IsNullOrWhiteSpace(twilio.AuthToken)
                || string.IsNullOrWhiteSpace(twilio.VerifyServiceSid)
                ? ValidateOptionsResult.Fail(
                    "Auth:PhoneVerification:Twilio:AccountSid, AuthToken and VerifyServiceSid are required.")
                : ValidateOptionsResult.Success;
        }

        return ValidateOptionsResult.Fail(
            $"Auth:PhoneVerification:Provider must be '{PhoneVerificationOptions.ProviderTwilio}' "
            + $"or '{PhoneVerificationOptions.ProviderFake}'.");
    }
}
