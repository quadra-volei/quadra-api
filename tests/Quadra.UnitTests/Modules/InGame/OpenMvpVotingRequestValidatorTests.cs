using FluentAssertions;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Validation;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="OpenMvpVotingRequestValidator"/>.
///
/// Covers (F1.5 AC-3 "voting deadline (set by Organizer)"): when supplied, the deadline must be
/// strictly in the future; a null deadline is valid (defaults to opened_at + 24h downstream).
/// </summary>
public sealed class OpenMvpVotingRequestValidatorTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private readonly OpenMvpVotingRequestValidator _validator = new(new FixedTimeProvider(FixedNow));

    /// <summary>Covers: F1.5 AC-3 — a null deadline is valid (server defaults it).</summary>
    [Fact]
    public void Validate_null_deadline_is_valid()
    {
        var result = _validator.Validate(new OpenMvpVotingRequest(DeadlineAt: null));
        result.IsValid.Should().BeTrue();
    }

    /// <summary>Covers: F1.5 AC-3 — a future deadline is valid.</summary>
    [Fact]
    public void Validate_future_deadline_is_valid()
    {
        var result = _validator.Validate(new OpenMvpVotingRequest(DeadlineAt: FixedNow.AddHours(1)));
        result.IsValid.Should().BeTrue();
    }

    /// <summary>Covers: F1.5 AC-3 — a past deadline is invalid.</summary>
    [Fact]
    public void Validate_past_deadline_is_invalid()
    {
        var result = _validator.Validate(new OpenMvpVotingRequest(DeadlineAt: FixedNow.AddHours(-1)));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(OpenMvpVotingRequest.DeadlineAt));
    }

    /// <summary>Covers: F1.5 AC-3 — a deadline exactly at 'now' is invalid (must be strictly future).</summary>
    [Fact]
    public void Validate_deadline_equal_to_now_is_invalid()
    {
        var result = _validator.Validate(new OpenMvpVotingRequest(DeadlineAt: FixedNow));
        result.IsValid.Should().BeFalse();
    }
}
