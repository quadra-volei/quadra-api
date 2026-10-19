using System.Threading.RateLimiting;
using Microsoft.Extensions.Options;
using Quadra.Modules.Auth.Configuration;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// Per-phone limits of the SMS login: codes sent per hour and code checks per 15 minutes (the
/// lockout after too many attempts). The per-IP limit is the ASP.NET Core rate limiter policy
/// <see cref="PolicyName"/>. In memory: single instance, counters reset on restart.
/// </summary>
public sealed class PhoneOtpThrottle : IDisposable
{
    public const string PolicyName = "sms-otp";

    private readonly PartitionedRateLimiter<string> _sends;
    private readonly PartitionedRateLimiter<string> _checks;

    public PhoneOtpThrottle(IOptions<AuthRateLimitOptions> options)
    {
        var o = options.Value;
        _sends = Create(o.CodesSentPerPhonePerHour, TimeSpan.FromHours(1));
        _checks = Create(o.CodeChecksPerPhonePer15Minutes, TimeSpan.FromMinutes(15));
    }

    public bool TryStart(string phoneNumber) => TryAcquire(_sends, phoneNumber);

    public bool TryCheck(string phoneNumber) => TryAcquire(_checks, phoneNumber);

    public void Dispose()
    {
        _sends.Dispose();
        _checks.Dispose();
    }

    private static bool TryAcquire(PartitionedRateLimiter<string> limiter, string phoneNumber)
    {
        using var lease = limiter.AttemptAcquire(phoneNumber);
        return lease.IsAcquired;
    }

    private static PartitionedRateLimiter<string> Create(int permits, TimeSpan window) =>
        PartitionedRateLimiter.Create<string, string>(phone => RateLimitPartition.GetFixedWindowLimiter(
            phone,
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = window, QueueLimit = 0 }));
}
