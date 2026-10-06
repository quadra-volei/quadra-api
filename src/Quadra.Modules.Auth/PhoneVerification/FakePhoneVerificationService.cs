using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;

namespace Quadra.Modules.Auth.PhoneVerification;

/// <summary>
/// Development/test <see cref="IPhoneVerificationService"/>: sends nothing and accepts the fixed
/// code from <c>Auth:PhoneVerification:Fake</c>. Startup validation refuses it in Production.
/// </summary>
public sealed class FakePhoneVerificationService : IPhoneVerificationService
{
    private readonly IOptionsMonitor<PhoneVerificationOptions> _options;

    public FakePhoneVerificationService(IOptionsMonitor<PhoneVerificationOptions> options)
    {
        _options = options;
    }

    public Task StartAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        EnsureAllowed(phoneNumber);
        return Task.CompletedTask;
    }

    public Task<PhoneVerificationResult> CheckAsync(
        string phoneNumber,
        string code,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        EnsureAllowed(phoneNumber);

        var expected = Encoding.UTF8.GetBytes(_options.CurrentValue.Fake.Code);
        var actual = Encoding.UTF8.GetBytes(code);
        var matches = CryptographicOperations.FixedTimeEquals(expected, actual);

        return Task.FromResult(matches
            ? PhoneVerificationResult.Approved
            : PhoneVerificationResult.InvalidCode);
    }

    private void EnsureAllowed(string phoneNumber)
    {
        var allowed = _options.CurrentValue.Fake.PhoneNumber;
        if (!string.IsNullOrWhiteSpace(allowed)
            && !string.Equals(allowed, phoneNumber, StringComparison.Ordinal))
        {
            throw new PhoneNumberRejectedException(
                "The fake phone verification only accepts the configured test number.");
        }
    }
}
