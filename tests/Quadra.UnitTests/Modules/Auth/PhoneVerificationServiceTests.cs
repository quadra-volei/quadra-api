using FluentAssertions;
using Quadra.Modules.Auth.Application;
using Quadra.Modules.Auth.Configuration;
using Quadra.Modules.Auth.PhoneVerification;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for the fixed-code fake <see cref="IPhoneVerificationService"/>.
/// </summary>
public sealed class PhoneVerificationServiceTests
{
    private const string Phone = "+5511999990000";

    // ---------------- fake ----------------

    private static FakePhoneVerificationService CreateFake(string? onlyPhoneNumber = null) =>
        new(AuthTestSupport.Monitor(new PhoneVerificationOptions
        {
            Provider = PhoneVerificationOptions.ProviderFake,
            Fake = new FakePhoneVerificationOptions { Code = "654321", PhoneNumber = onlyPhoneNumber },
        }));

    /// <summary>
    /// Covers FA.3: "fake implementation with a fixed, configurable code".
    /// </summary>
    [Theory]
    [InlineData("654321", PhoneVerificationResult.Approved)]
    [InlineData("123456", PhoneVerificationResult.InvalidCode)]
    public async Task Fake_approves_only_the_configured_code(string code, PhoneVerificationResult expected)
    {
        var sut = CreateFake();

        await sut.StartAsync(Phone, TestContext.Current.CancellationToken);
        var result = await sut.CheckAsync(Phone, code, TestContext.Current.CancellationToken);

        result.Should().Be(expected);
    }

    /// <summary>
    /// Covers FA.3: "fake implementation with a fixed, configurable number" — when a number is
    /// configured, every other number is refused.
    /// </summary>
    [Fact]
    public async Task Fake_with_configured_number_rejects_other_numbers()
    {
        var sut = CreateFake(onlyPhoneNumber: Phone);

        await sut.StartAsync(Phone, TestContext.Current.CancellationToken);

        var start = async () => await sut.StartAsync("+5511888880000", TestContext.Current.CancellationToken);
        var check = async () => await sut.CheckAsync("+5511888880000", "654321", TestContext.Current.CancellationToken);

        await start.Should().ThrowAsync<PhoneNumberRejectedException>();
        await check.Should().ThrowAsync<PhoneNumberRejectedException>();
    }
}
