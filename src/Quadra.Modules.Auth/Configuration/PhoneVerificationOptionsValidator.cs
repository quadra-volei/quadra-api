using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// Enforces the <c>Auth:PhoneVerification</c> settings at application startup: a known provider,
/// the Firebase project when Firebase is selected, and no fake provider in Production.
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
            // Allow-list, not deny-list: any other environment name (Staging, a typo) would let anyone
            // log in as any phone with the fixed code (audit 2026-11-24, fake-phone-provider-deny-list-gate).
            if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"))
            {
                return ValidateOptionsResult.Fail(
                    "Auth:PhoneVerification:Provider 'Fake' is allowed only in Development and Testing.");
            }

            return string.IsNullOrWhiteSpace(options.Fake.Code)
                ? ValidateOptionsResult.Fail("Auth:PhoneVerification:Fake:Code is required.")
                : ValidateOptionsResult.Success;
        }

        if (options.UsesFirebase)
        {
            return string.IsNullOrWhiteSpace(options.Firebase.ProjectId)
                ? ValidateOptionsResult.Fail("Auth:PhoneVerification:Firebase:ProjectId is required when the provider is Firebase.")
                : ValidateOptionsResult.Success;
        }


        return ValidateOptionsResult.Fail(
            $"Auth:PhoneVerification:Provider must be '{PhoneVerificationOptions.ProviderFirebase}' "
            + $"or '{PhoneVerificationOptions.ProviderFake}'.");
    }
}
