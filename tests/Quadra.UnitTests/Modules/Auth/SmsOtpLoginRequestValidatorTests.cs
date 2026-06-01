using FluentAssertions;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Validation;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="SmsOtpLoginRequestValidator"/> (FA.3). The endpoint is a single route
/// discriminated by <c>Step</c>; the <c>verify</c> step adds Code/Session requirements.
/// </summary>
public sealed class SmsOtpLoginRequestValidatorTests
{
    private readonly SmsOtpLoginRequestValidator _sut = new();

    private static SmsOtpLoginRequest ValidInitiate() => new(
        Step: "initiate",
        PhoneNumber: "+5511999999999",
        Code: null,
        Session: null,
        DeviceId: null);

    private static SmsOtpLoginRequest ValidVerify() => new(
        Step: "verify",
        PhoneNumber: "+5511999999999",
        Code: "123456",
        Session: "opaque-session",
        DeviceId: "device-1");

    /// <summary>
    /// Covers FA.3 validation: "Step: required; one of initiate, verify".
    /// </summary>
    [Fact]
    public void Initiate_with_valid_phone_passes()
    {
        var result = _sut.Validate(ValidInitiate());

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Covers FA.3 validation: a valid verify request (code + session present) passes.
    /// </summary>
    [Fact]
    public void Verify_with_code_and_session_passes()
    {
        var result = _sut.Validate(ValidVerify());

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Covers FA.3: "Step required; one of initiate/verify (case-insensitive, normalized lowercase)".
    /// </summary>
    [Theory]
    [InlineData("INITIATE")]
    [InlineData("Verify")]
    [InlineData("  verify  ")]
    public void Step_is_case_insensitive_and_trimmed(string step)
    {
        var request = step.Trim().Equals("verify", StringComparison.OrdinalIgnoreCase)
            ? ValidVerify() with { Step = step }
            : ValidInitiate() with { Step = step };

        var result = _sut.Validate(request);

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Covers FA.3 400: "unknown Step".
    /// </summary>
    [Fact]
    public void Unknown_step_fails()
    {
        var result = _sut.Validate(ValidInitiate() with { Step = "bogus" });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SmsOtpLoginRequest.Step));
    }

    /// <summary>
    /// Covers FA.3 400: "Step: required".
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_step_fails(string step)
    {
        var result = _sut.Validate(ValidInitiate() with { Step = step });

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// Covers FA.3 validation: "PhoneNumber: required; matches E.164 regex".
    /// </summary>
    [Theory]
    [InlineData("5511999999999")] // missing +
    [InlineData("+0511999999999")] // leading zero after +
    [InlineData("+")]
    [InlineData("not-a-phone")]
    [InlineData("")]
    public void Bad_e164_fails(string phone)
    {
        var result = _sut.Validate(ValidInitiate() with { PhoneNumber = phone });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SmsOtpLoginRequest.PhoneNumber));
    }

    /// <summary>
    /// Covers FA.3 400: "verify missing Code".
    /// </summary>
    [Fact]
    public void Verify_without_code_fails()
    {
        var result = _sut.Validate(ValidVerify() with { Code = null });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SmsOtpLoginRequest.Code));
    }

    /// <summary>
    /// Covers FA.3 validation: "Code required, exactly 6 digits".
    /// </summary>
    [Theory]
    [InlineData("12345")] // too short
    [InlineData("1234567")] // too long
    [InlineData("12a456")] // non-digit
    [InlineData("abcdef")]
    public void Verify_with_malformed_code_fails(string code)
    {
        var result = _sut.Validate(ValidVerify() with { Code = code });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SmsOtpLoginRequest.Code));
    }

    /// <summary>
    /// Covers FA.3 400: "verify missing Session".
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Verify_without_session_fails(string? session)
    {
        var result = _sut.Validate(ValidVerify() with { Session = session });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SmsOtpLoginRequest.Session));
    }

    /// <summary>
    /// Covers FA.3 validation: "Session ... max 4096 chars".
    /// </summary>
    [Fact]
    public void Verify_with_oversized_session_fails()
    {
        var result = _sut.Validate(ValidVerify() with { Session = new string('s', 4097) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SmsOtpLoginRequest.Session));
    }

    /// <summary>
    /// Covers FA.3 validation: "DeviceId: optional; max 128 chars when present".
    /// </summary>
    [Fact]
    public void Verify_with_oversized_device_id_fails()
    {
        var result = _sut.Validate(ValidVerify() with { DeviceId = new string('d', 129) });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SmsOtpLoginRequest.DeviceId));
    }

    /// <summary>
    /// Covers FA.3: the initiate step does NOT require Code/Session (they are verify-only).
    /// </summary>
    [Fact]
    public void Initiate_without_code_or_session_passes()
    {
        var result = _sut.Validate(ValidInitiate() with { Code = null, Session = null });

        result.IsValid.Should().BeTrue();
    }
}
