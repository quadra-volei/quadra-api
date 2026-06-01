using FluentAssertions;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Validation;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="OidcLoginRequestValidator"/> (FA.3, shared by Google + Apple login).
/// </summary>
public sealed class OidcLoginRequestValidatorTests
{
    private readonly OidcLoginRequestValidator _sut = new();

    /// <summary>
    /// Covers FA.3 validation: "IdToken: required, non-empty, max 8192 chars" — happy path.
    /// </summary>
    [Fact]
    public void Valid_token_passes()
    {
        var result = _sut.Validate(new OidcLoginRequest("eyJ.fake.token", DeviceId: null));

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Covers FA.3 400: "empty token".
    /// </summary>
    [Theory]
    [InlineData("")]
    public void Empty_token_fails(string token)
    {
        var result = _sut.Validate(new OidcLoginRequest(token, DeviceId: null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(OidcLoginRequest.IdToken));
    }

    /// <summary>
    /// Covers FA.3 400: "oversized token" (max 8192).
    /// </summary>
    [Fact]
    public void Oversized_token_fails()
    {
        var result = _sut.Validate(new OidcLoginRequest(new string('t', 8193), DeviceId: null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(OidcLoginRequest.IdToken));
    }

    /// <summary>
    /// Covers FA.3 validation: "DeviceId: optional, max 128 chars" — present and within bound passes.
    /// </summary>
    [Fact]
    public void Device_id_within_bound_passes()
    {
        var result = _sut.Validate(new OidcLoginRequest("eyJ.fake.token", new string('d', 128)));

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Covers FA.3 400: "DeviceId max 128 chars".
    /// </summary>
    [Fact]
    public void Oversized_device_id_fails()
    {
        var result = _sut.Validate(new OidcLoginRequest("eyJ.fake.token", new string('d', 129)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(OidcLoginRequest.DeviceId));
    }
}
