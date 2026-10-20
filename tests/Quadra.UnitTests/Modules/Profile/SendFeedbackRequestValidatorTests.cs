using FluentAssertions;
using Quadra.Modules.Profile.Feedback;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>Validation rules of <c>POST /api/v1/feedback</c>.</summary>
public sealed class SendFeedbackRequestValidatorTests
{
    private readonly SendFeedbackRequestValidator _sut = new();

    [Theory]
    [InlineData("suggestion", "Poderia ter modo escuro.", null, null)]
    [InlineData("Problem", "  O placar travou.  ", "1.0.0", "android")]
    [InlineData("PRAISE", "Muito bom!", "1.2.3", "iOS")]
    public void Valid_requests_pass(string type, string message, string? appVersion, string? platform)
    {
        _sut.Validate(new SendFeedbackRequest(type, message, appVersion, platform)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("complaint")]
    [InlineData("1")]
    public void Unknown_type_fails(string? type)
    {
        _sut.Validate(new SendFeedbackRequest(type, "texto")).Errors
            .Should().Contain(e => e.PropertyName == nameof(SendFeedbackRequest.Type));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Empty_message_fails(string? message)
    {
        _sut.Validate(new SendFeedbackRequest("problem", message)).Errors
            .Should().Contain(e => e.PropertyName == nameof(SendFeedbackRequest.Message));
    }

    [Fact]
    public void Message_is_limited_to_1000_characters_after_trimming()
    {
        var atLimit = new string('a', FeedbackEntry.MessageMaxLength);

        _sut.Validate(new SendFeedbackRequest("problem", $"  {atLimit}  ")).IsValid.Should().BeTrue();
        _sut.Validate(new SendFeedbackRequest("problem", atLimit + "a")).IsValid.Should().BeFalse();
    }

    [Fact]
    public void Unknown_platform_and_oversized_version_fail()
    {
        var result = _sut.Validate(new SendFeedbackRequest("problem", "texto", new string('1', 33), "windows-phone"));

        result.Errors.Select(e => e.PropertyName).Should().Contain(
        [
            nameof(SendFeedbackRequest.AppVersion),
            nameof(SendFeedbackRequest.Platform),
        ]);
    }
}
