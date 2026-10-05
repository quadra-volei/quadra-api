namespace Quadra.Modules.Auth.PhoneVerification;

/// <summary>
/// Outcome of checking an SMS OTP code.
/// </summary>
public enum PhoneVerificationResult
{
    /// <summary>The code matched; the phone number is proven.</summary>
    Approved = 0,

    /// <summary>A verification is pending but the code is wrong.</summary>
    InvalidCode = 1,

    /// <summary>No pending verification exists (expired, already approved, or never started).</summary>
    Expired = 2,
}

/// <summary>
/// Delivers and checks the SMS one-time code that proves ownership of a phone number. The
/// provider owns the code itself (generation, expiry, attempt limits); the API never sees or
/// stores it. Implementations: Twilio Verify and a fixed-code fake for development and tests.
/// </summary>
public interface IPhoneVerificationService
{
    /// <summary>
    /// Sends a code by SMS to <paramref name="phoneNumber"/> (E.164). Calling it again resends.
    /// Throws <see cref="Application.PhoneNumberRejectedException"/>,
    /// <see cref="Application.PhoneVerificationThrottledException"/> or
    /// <see cref="Application.PhoneVerificationUnavailableException"/>.
    /// </summary>
    Task StartAsync(string phoneNumber, CancellationToken cancellationToken);

    /// <summary>
    /// Checks <paramref name="code"/> against the pending verification for
    /// <paramref name="phoneNumber"/>. Throws
    /// <see cref="Application.PhoneVerificationThrottledException"/> or
    /// <see cref="Application.PhoneVerificationUnavailableException"/>.
    /// </summary>
    Task<PhoneVerificationResult> CheckAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken);
}
