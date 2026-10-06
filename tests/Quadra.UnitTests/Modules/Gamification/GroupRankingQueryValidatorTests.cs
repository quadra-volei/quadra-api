using FluentAssertions;
using FluentValidation.TestHelper;
using Quadra.Modules.Gamification.Contracts;
using Quadra.Modules.Gamification.Validation;

namespace Quadra.UnitTests.Modules.Gamification;

/// <summary>
/// Unit tests for <see cref="GroupRankingQueryValidator"/> — the pagination bounds
/// (Page &gt;= 1; 1 &lt;= PageSize &lt;= 50). These cover the out-of-range cases that the endpoint
/// maps to 400 (AC-10/AC-11/AC-12); the HTTP 400 mapping itself is additionally verified in the
/// endpoint integration tests.
/// </summary>
public sealed class GroupRankingQueryValidatorTests
{
    private readonly GroupRankingQueryValidator _validator = new();

    /// <summary>Covers AC-10 (pageSize lower bound): PageSize = 0 is invalid.</summary>
    [Fact]
    public void PageSize_zero_is_invalid()
    {
        var result = _validator.TestValidate(new GroupRankingQuery(Page: 1, PageSize: 0));
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    /// <summary>Covers AC-11 (pageSize upper bound): PageSize = 51 is invalid.</summary>
    [Fact]
    public void PageSize_above_fifty_is_invalid()
    {
        var result = _validator.TestValidate(new GroupRankingQuery(Page: 1, PageSize: 51));
        result.ShouldHaveValidationErrorFor(x => x.PageSize);
    }

    /// <summary>Covers AC-12 (page lower bound): Page = 0 is invalid.</summary>
    [Fact]
    public void Page_zero_is_invalid()
    {
        var result = _validator.TestValidate(new GroupRankingQuery(Page: 0, PageSize: 20));
        result.ShouldHaveValidationErrorFor(x => x.Page);
    }

    /// <summary>Supports AC-10/11/12: the boundary values (page 1, pageSize 1 and 50) are valid.</summary>
    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 50)]
    [InlineData(3, 20)]
    public void Valid_boundaries_pass(int page, int pageSize)
    {
        var result = _validator.TestValidate(new GroupRankingQuery(page, pageSize));
        result.IsValid.Should().BeTrue();
    }
}
