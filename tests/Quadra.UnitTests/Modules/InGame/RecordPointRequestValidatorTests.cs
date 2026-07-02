using FluentAssertions;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Validation;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="RecordPointRequestValidator"/>.
///
/// Covers (F1.4 validation): TeamId must not be empty.
/// </summary>
public sealed class RecordPointRequestValidatorTests
{
    private readonly RecordPointRequestValidator _sut = new();

    /// <summary>Covers: F1.4 — a non-empty TeamId passes validation.</summary>
    [Fact]
    public void Non_empty_teamId_passes()
    {
        var result = _sut.Validate(new RecordPointRequest(Guid.NewGuid()));
        result.IsValid.Should().BeTrue();
    }

    /// <summary>Covers: F1.4 — an empty TeamId fails validation (400).</summary>
    [Fact]
    public void Empty_teamId_fails()
    {
        var result = _sut.Validate(new RecordPointRequest(Guid.Empty));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(RecordPointRequest.TeamId));
    }
}
