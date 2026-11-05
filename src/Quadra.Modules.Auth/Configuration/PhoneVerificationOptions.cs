namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// Options bound to <c>Auth:PhoneVerification</c>. <see cref="Provider"/> selects which
/// <see cref="PhoneVerification.IPhoneVerificationService"/> implementation delivers and checks
/// the SMS OTP.
/// </summary>
public sealed class PhoneVerificationOptions
{
    public const string SectionName = "Auth:PhoneVerification";

    public const string ProviderFake = "Fake";
    public const string ProviderFirebase = "Firebase";

    /// <summary>
    /// <c>Firebase</c> (the app verifies the phone with
    /// Firebase Phone Auth and sends its ID token to <c>login/firebase-phone</c>; <c>login/sms-otp</c> is
    /// then off) or <c>Fake</c> (fixed code, no SMS;
    /// refused in Production).
    /// </summary>
    public string Provider { get; set; } = ProviderFirebase;

    public FakePhoneVerificationOptions Fake { get; set; } = new();

    public FirebasePhoneOptions Firebase { get; set; } = new();

    public bool UsesFake =>
        string.Equals(Provider, ProviderFake, StringComparison.OrdinalIgnoreCase);

    public bool UsesFirebase =>
        string.Equals(Provider, ProviderFirebase, StringComparison.OrdinalIgnoreCase);
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

/// <summary>
/// Firebase Phone Auth (DECISIONS #78). With <see cref="ProjectId"/> set, <c>login/firebase-phone</c>
/// accepts Firebase ID tokens of that project whose sign-in was by phone.
/// </summary>
public sealed class FirebasePhoneOptions
{
    /// <summary>The Firebase project id (e.g. <c>quadra-volei-app</c>); empty = route off.</summary>
    public string ProjectId { get; set; } = string.Empty;
}
