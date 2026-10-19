using FluentAssertions;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Validation;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="CreateScoreboardRequestValidator"/>.
///
/// Covers (F1.4 validation): Format is required and must be BestOf3 or BestOf5 (case-insensitive).
/// </summary>
public sealed class CreateScoreboardRequestValidatorTests
{
    private readonly CreateScoreboardRequestValidator _sut = new();

    /// <summary>Covers: F1.4 — BestOf3 / BestOf5 are accepted (case-insensitive).</summary>
    [Theory]
    [InlineData("BestOf3")]
    [InlineData("BestOf5")]
    [InlineData("bestof3")]
    [InlineData("BESTOF5")]
    public void Valid_format_passes(string format)
    {
        var result = _sut.Validate(new CreateScoreboardRequest(format));
        result.IsValid.Should().BeTrue();
    }

    /// <summary>Covers: an empty Format fails (400); no Format at all means a single game (DECISIONS #72).</summary>
    [Fact]
    public void Empty_format_fails_and_missing_format_is_a_single_game()
    {
        var empty = _sut.Validate(new CreateScoreboardRequest(""));
        empty.IsValid.Should().BeFalse();
        empty.Errors.Should().Contain(e => e.PropertyName == nameof(CreateScoreboardRequest.Format));
        _sut.Validate(new CreateScoreboardRequest()).IsValid.Should().BeTrue();
    }


    /// <summary>Covers: F1.4 — an unrecognised Format value fails validation (400).</summary>
    [Theory]
    [InlineData("BestOf7")]
    [InlineData("bo3")]
    [InlineData("Single")]
    public void Invalid_format_fails(string format)
    {
        var result = _sut.Validate(new CreateScoreboardRequest(format));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.ErrorMessage.Contains("BestOf3"));
    }
}
