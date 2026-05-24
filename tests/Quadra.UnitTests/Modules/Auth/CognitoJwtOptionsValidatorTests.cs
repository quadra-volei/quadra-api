using FluentAssertions;
using FluentValidation.Results;
using Quadra.Modules.Auth.Configuration;

namespace Quadra.UnitTests.Modules.Auth;

/// <summary>
/// Covers acceptance criterion #4 — Configuration via appsettings:
/// CognitoJwtOptionsValidator rejects missing/blank UserPoolId, Region, Audience,
/// and rejects negative ClockSkewSeconds.
/// </summary>
public class CognitoJwtOptionsValidatorTests
{
    private readonly CognitoJwtOptionsValidator _sut = new();

    private static CognitoJwtOptions ValidOptions() => new()
    {
        UserPoolId = "us-east-1_ABCdEfGhI",
        Region = "us-east-1",
        Audience = "client-id-123",
        ClockSkewSeconds = 30,
    };

    [Fact]
    public void Validate_with_fully_populated_options_succeeds()
    {
        ValidationResult result = _sut.Validate(ValidOptions());

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_rejects_missing_or_blank_UserPoolId(string? value)
    {
        var options = ValidOptions();
        options.UserPoolId = value!;

        ValidationResult result = _sut.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CognitoJwtOptions.UserPoolId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_rejects_missing_or_blank_Region(string? value)
    {
        var options = ValidOptions();
        options.Region = value!;

        ValidationResult result = _sut.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CognitoJwtOptions.Region));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Validate_rejects_missing_or_blank_Audience(string? value)
    {
        var options = ValidOptions();
        options.Audience = value!;

        ValidationResult result = _sut.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CognitoJwtOptions.Audience));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-30)]
    [InlineData(int.MinValue)]
    public void Validate_rejects_negative_ClockSkewSeconds(int value)
    {
        var options = ValidOptions();
        options.ClockSkewSeconds = value;

        ValidationResult result = _sut.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CognitoJwtOptions.ClockSkewSeconds));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(300)]
    public void Validate_accepts_non_negative_ClockSkewSeconds(int value)
    {
        var options = ValidOptions();
        options.ClockSkewSeconds = value;

        ValidationResult result = _sut.Validate(options);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_collects_all_missing_required_field_errors_at_once()
    {
        var options = new CognitoJwtOptions
        {
            UserPoolId = string.Empty,
            Region = string.Empty,
            Audience = string.Empty,
            ClockSkewSeconds = 30,
        };

        ValidationResult result = _sut.Validate(options);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(e => e.PropertyName).Should().BeEquivalentTo(
            nameof(CognitoJwtOptions.UserPoolId),
            nameof(CognitoJwtOptions.Region),
            nameof(CognitoJwtOptions.Audience));
    }
}
