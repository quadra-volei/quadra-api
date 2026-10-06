namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// Options bound to <c>Auth:PhoneVerification</c>. <see cref="Provider"/> selects which
/// <see cref="PhoneVerification.IPhoneVerificationService"/> implementation delivers and checks
/// the SMS OTP.
/// </summary>
public sealed class PhoneVerificationOptions
{
    public const string SectionName = "Auth:PhoneVerification";

    public const string ProviderTwilio = "Twilio";
    public const string ProviderFake = "Fake";

    /// <summary>
    /// <c>Twilio</c> (real SMS through Twilio Verify) or <c>Fake</c> (fixed code, no SMS;
    /// refused in Production).
    /// </summary>
    public string Provider { get; set; } = ProviderTwilio;

    public TwilioVerifyOptions Twilio { get; set; } = new();

    public FakePhoneVerificationOptions Fake { get; set; } = new();

    public bool UsesFake =>
        string.Equals(Provider, ProviderFake, StringComparison.OrdinalIgnoreCase);

    public bool UsesTwilio =>
        string.Equals(Provider, ProviderTwilio, StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Twilio Verify credentials. The Verify Service itself (code length = 6, expiry, SMS template)
/// is configured in the Twilio console.
/// </summary>
public sealed class TwilioVerifyOptions
{
    public string AccountSid { get; set; } = string.Empty;

    public string AuthToken { get; set; } = string.Empty;

    /// <summary>
    /// SID of the Verify Service (starts with <c>VA</c>).
    /// </summary>
    public string VerifyServiceSid { get; set; } = string.Empty;
}

/// <summary>
/// Settings for the development/test fake. No SMS is ever sent.
/// </summary>
public sealed class FakePhoneVerificationOptions
{
    /// <summary>
    /// The only code the fake accepts.
    /// </summary>
    public string Code { get; set; } = "123456";

    /// <summary>
    /// Optional E.164 number. When set, only this number can log in; when empty, any number is
    /// accepted with <see cref="Code"/>.
    /// </summary>
    public string? PhoneNumber { get; set; }
}
