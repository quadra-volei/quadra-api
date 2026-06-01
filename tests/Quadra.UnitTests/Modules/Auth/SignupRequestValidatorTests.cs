using FluentAssertions;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Validation;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="SignupRequestValidator"/>. Covers FA.2 validation rules
/// (acceptance criterion: "400 — request fails validation" — every shape of bad input).
/// </summary>
public sealed class SignupRequestValidatorTests
{
    private readonly SignupRequestValidator _sut = new();

    private static SignupRequest ValidPhone() => new(
        Provider: "phone",
        Phone: new PhoneSignupPayload("+5511999999999"),
        Google: null,
        Apple: null);

    private static SignupRequest ValidGoogle() => new(
        Provider: "google",
        Phone: null,
        Google: new OidcSignupPayload("eyJ.fake.token"),
        Apple: null);

    private static SignupRequest ValidApple() => new(
        Provider: "apple",
        Phone: null,
        Google: null,
        Apple: new OidcSignupPayload("eyJ.fake.token"));

    // -------- happy paths --------

    /// <summary>
    /// Covers: valid phone payload passes validation.
    /// </summary>
    [Fact]
    public void Phone_with_valid_E164_passes()
    {
        var result = _sut.Validate(ValidPhone());
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Covers: valid Google payload passes validation.
    /// </summary>
    [Fact]
    public void Google_with_idtoken_passes()
    {
        var result = _sut.Validate(ValidGoogle());
        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Covers: valid Apple payload passes validation.
    /// </summary>
    [Fact]
    public void Apple_with_idtoken_passes()
    {
        var result = _sut.Validate(ValidApple());
        result.IsValid.Should().BeTrue();
    }

    // -------- provider field --------

    /// <summary>
    /// Covers: Provider is required.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Provider_blank_is_rejected(string provider)
    {
        var req = new SignupRequest(provider, new PhoneSignupPayload("+5511999999999"), null, null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SignupRequest.Provider));
    }

    /// <summary>
    /// Covers: Provider must be one of phone/google/apple.
    /// </summary>
    [Fact]
    public void Provider_unknown_value_is_rejected()
    {
        var req = new SignupRequest("facebook", null, null, null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SignupRequest.Provider));
    }

    // -------- phone branch --------

    /// <summary>
    /// Covers: "When Provider == phone: Phone payload required".
    /// </summary>
    [Fact]
    public void Phone_provider_without_payload_is_rejected()
    {
        var req = new SignupRequest("phone", null, null, null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SignupRequest.Phone));
    }

    /// <summary>
    /// Covers: "When Provider == phone: Google and Apple must be null".
    /// </summary>
    [Fact]
    public void Phone_provider_with_google_payload_is_rejected()
    {
        var req = new SignupRequest(
            "phone",
            new PhoneSignupPayload("+5511999999999"),
            new OidcSignupPayload("x"),
            null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SignupRequest.Google));
    }

    /// <summary>
    /// Covers: "PhoneNumber must match E.164 regex ^\+[1-9]\d{1,14}$".
    /// </summary>
    [Theory]
    [InlineData("5511999999999")]    // missing +
    [InlineData("+0511999999999")]   // leading 0 after +
    [InlineData("+")]                // just +
    [InlineData("+abc12345")]        // non-digits
    [InlineData("+5511999999999999999")] // too long (>15 digits)
    public void Phone_invalid_E164_is_rejected(string number)
    {
        var req = new SignupRequest("phone", new PhoneSignupPayload(number), null, null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
    }

    // -------- google branch --------

    /// <summary>
    /// Covers: "When Provider == google: Google payload required".
    /// </summary>
    [Fact]
    public void Google_provider_without_payload_is_rejected()
    {
        var req = new SignupRequest("google", null, null, null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SignupRequest.Google));
    }

    /// <summary>
    /// Covers: "Google.IdToken non-empty".
    /// </summary>
    [Fact]
    public void Google_empty_id_token_is_rejected()
    {
        var req = new SignupRequest("google", null, new OidcSignupPayload(string.Empty), null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// Covers: "Google.IdToken max 8192 chars".
    /// </summary>
    [Fact]
    public void Google_idtoken_exceeding_max_length_is_rejected()
    {
        var req = new SignupRequest(
            "google",
            null,
            new OidcSignupPayload(new string('a', 8193)),
            null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// Covers: "When Provider == google: Phone and Apple must be null".
    /// </summary>
    [Fact]
    public void Google_provider_with_phone_payload_is_rejected()
    {
        var req = new SignupRequest(
            "google",
            new PhoneSignupPayload("+5511999999999"),
            new OidcSignupPayload("x"),
            null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SignupRequest.Phone));
    }

    // -------- apple branch --------

    /// <summary>
    /// Covers: "When Provider == apple: Apple payload required".
    /// </summary>
    [Fact]
    public void Apple_provider_without_payload_is_rejected()
    {
        var req = new SignupRequest("apple", null, null, null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SignupRequest.Apple));
    }

    /// <summary>
    /// Covers: "Apple.IdToken non-empty, max 8192".
    /// </summary>
    [Fact]
    public void Apple_empty_id_token_is_rejected()
    {
        var req = new SignupRequest("apple", null, null, new OidcSignupPayload(string.Empty));

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
    }

    /// <summary>
    /// Covers: "When Provider == apple: Phone and Google must be null".
    /// </summary>
    [Fact]
    public void Apple_provider_with_google_payload_is_rejected()
    {
        var req = new SignupRequest(
            "apple",
            null,
            new OidcSignupPayload("x"),
            new OidcSignupPayload("y"));

        var result = _sut.Validate(req);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(SignupRequest.Google));
    }

    /// <summary>
    /// Covers: provider matching is case-insensitive (validator normalizes).
    /// </summary>
    [Theory]
    [InlineData("PHONE")]
    [InlineData("Phone")]
    [InlineData("pHoNe")]
    public void Provider_is_case_insensitive(string provider)
    {
        var req = new SignupRequest(provider, new PhoneSignupPayload("+5511999999999"), null, null);

        var result = _sut.Validate(req);

        result.IsValid.Should().BeTrue();
    }
}
