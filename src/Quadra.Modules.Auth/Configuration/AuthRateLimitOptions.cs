namespace Quadra.Modules.Auth.Configuration;

/// <summary>
/// Options bound to <c>Auth:RateLimits</c>: how often the SMS login can be used, per client IP and
/// per phone number (SMS cost and code brute force). Counters live in memory: one API instance.
/// </summary>
public sealed class AuthRateLimitOptions
{
    public const string SectionName = "Auth:RateLimits";

    /// <summary>Calls to <c>login/sms-otp</c> (both steps) per client IP per hour.</summary>
    public int SmsOtpPerIpPerHour { get; set; } = 30;

    /// <summary>Codes sent to one phone number per hour.</summary>
    public int CodesSentPerPhonePerHour { get; set; } = 5;

    /// <summary>Code checks for one phone number per 15 minutes; past it the number is locked out.</summary>
    public int CodeChecksPerPhonePer15Minutes { get; set; } = 5;
}
