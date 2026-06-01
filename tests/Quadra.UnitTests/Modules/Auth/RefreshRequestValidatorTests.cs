using FluentAssertions;
using Quadra.Modules.Auth.Contracts;
using Quadra.Modules.Auth.Validation;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Unit tests for <see cref="RefreshRequestValidator"/> (FA.3).
/// </summary>
public sealed class RefreshRequestValidatorTests
{
    private readonly RefreshRequestValidator _sut = new();

    /// <summary>
    /// Covers FA.3 validation: "RefreshToken: required, non-empty, max 4096 chars" — happy path.
    /// </summary>
    [Fact]
    public void Valid_token_passes()
    {
        var result = _sut.Validate(new RefreshRequest("some-refresh-token", DeviceId: null));

        result.IsValid.Should().BeTrue();
    }

    /// <summary>
    /// Covers FA.3 400: "empty token".
    /// </summary>
    [Fact]
    public void Empty_token_fails()
    {
        var result = _sut.Validate(new RefreshRequest(string.Empty, DeviceId: null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RefreshRequest.RefreshToken));
    }

    /// <summary>
    /// Covers FA.3 validation: "RefreshToken ... max 4096 chars".
    /// </summary>
    [Fact]
    public void Oversized_token_fails()
    {
        var result = _sut.Validate(new RefreshRequest(new string('t', 4097), DeviceId: null));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RefreshRequest.RefreshToken));
    }

    /// <summary>
    /// Covers FA.3 validation: "DeviceId: optional, max 128 chars".
    /// </summary>
    [Fact]
    public void Oversized_device_id_fails()
    {
        var result = _sut.Validate(new RefreshRequest("tok", new string('d', 129)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RefreshRequest.DeviceId));
    }
}
