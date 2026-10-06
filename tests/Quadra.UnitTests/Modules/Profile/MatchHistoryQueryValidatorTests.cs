using FluentAssertions;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Validation;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>
/// Unit tests for <see cref="MatchHistoryQueryValidator"/>.
///
/// Covers: F2.1 AC "match history with pagination" — Page &gt;= 1 and 1 &lt;= PageSize &lt;= 50.
/// </summary>
public sealed class MatchHistoryQueryValidatorTests
{
    private readonly MatchHistoryQueryValidator _sut = new();

    /// <summary>Covers: the default query (page 1, size 20) is valid.</summary>
    [Fact]
    public void Default_query_passes()
    {
        _sut.Validate(new MatchHistoryQuery()).IsValid.Should().BeTrue();
    }

    /// <summary>Covers: Page boundary — 1 is valid, 0 and negative are not.</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    [InlineData(-3, false)]
    public void Page_bounds_are_enforced(int page, bool expectedValid)
    {
        _sut.Validate(new MatchHistoryQuery(page, 20)).IsValid.Should().Be(expectedValid);
    }

    /// <summary>Covers: PageSize boundary — 1 and 50 are valid, 0 and 51 are not.</summary>
    [Theory]
    [InlineData(1, true)]
    [InlineData(50, true)]
    [InlineData(0, false)]
    [InlineData(51, false)]
    public void PageSize_bounds_are_enforced(int pageSize, bool expectedValid)
    {
        _sut.Validate(new MatchHistoryQuery(1, pageSize)).IsValid.Should().Be(expectedValid);
    }
}
