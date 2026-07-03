using FluentAssertions;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Validation;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>
/// Unit tests for <see cref="PhotoUploadUrlRequestValidator"/>.
///
/// Covers: F2.1 AC "photo upload-url endpoint" — ContentType must be one of the supported image
/// types (image/jpeg, image/png, image/webp).
/// </summary>
public sealed class PhotoUploadUrlRequestValidatorTests
{
    private readonly PhotoUploadUrlRequestValidator _sut = new();

    /// <summary>Covers: every supported content type is accepted.</summary>
    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    [InlineData("image/webp")]
    public void Supported_content_type_passes(string contentType)
    {
        _sut.Validate(new PhotoUploadUrlRequest(contentType)).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: an unsupported content type is rejected.</summary>
    [Theory]
    [InlineData("image/gif")]
    [InlineData("application/pdf")]
    [InlineData("text/plain")]
    public void Unsupported_content_type_is_invalid(string contentType)
    {
        _sut.Validate(new PhotoUploadUrlRequest(contentType)).IsValid.Should().BeFalse();
    }

    /// <summary>Covers: an empty content type is rejected.</summary>
    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_content_type_is_invalid(string contentType)
    {
        _sut.Validate(new PhotoUploadUrlRequest(contentType)).IsValid.Should().BeFalse();
    }
}
