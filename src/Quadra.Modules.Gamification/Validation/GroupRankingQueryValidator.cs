using FluentValidation;
using Quadra.Modules.Gamification.Contracts;

namespace Quadra.Modules.Gamification.Validation;

/// <summary>
/// Validates the group-ranking pagination query: <c>Page &gt;= 1</c> and <c>1 &lt;= PageSize &lt;= 50</c>.
/// </summary>
public sealed class GroupRankingQueryValidator : AbstractValidator<GroupRankingQuery>
{
    public GroupRankingQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be 1 or greater.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50)
            .WithMessage("PageSize must be between 1 and 50.");
    }
}
