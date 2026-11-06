using FluentAssertions;
using Quadra.Modules.Profile.Tour;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>Unit tests for <see cref="RecordTourRequestValidator"/> (first-access tour record).</summary>
public sealed class RecordTourRequestValidatorTests
{
    private readonly RecordTourRequestValidator _sut = new();

    [Theory]
    [InlineData(1, "Completed", 3)]
    [InlineData(1, "Skipped", 0)]
    [InlineData(RecordTourRequestValidator.MaxVersion, "Skipped", RecordTourRequestValidator.MaxStep)]
    public void Well_formed_record_passes(int version, string outcome, int lastStep)
    {
        _sut.Validate(new RecordTourRequest(version, outcome, lastStep)).IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(null, "Completed", 3)]
    [InlineData(0, "Completed", 3)]
    [InlineData(RecordTourRequestValidator.MaxVersion + 1, "Completed", 3)]
    [InlineData(1, null, 3)]
    [InlineData(1, "Done", 3)]
    [InlineData(1, "completed", 3)]
    [InlineData(1, "0", 3)]
    [InlineData(1, "Completed", null)]
    [InlineData(1, "Completed", -1)]
    [InlineData(1, "Completed", RecordTourRequestValidator.MaxStep + 1)]
    public void Malformed_record_is_invalid(int? version, string? outcome, int? lastStep)
    {
        _sut.Validate(new RecordTourRequest(version, outcome, lastStep)).IsValid.Should().BeFalse();
    }
}
