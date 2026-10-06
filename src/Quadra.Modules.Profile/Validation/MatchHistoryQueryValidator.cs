using FluentValidation;
using Quadra.Modules.Profile.Contracts;

namespace Quadra.Modules.Profile.Validation;

/// <summary>
/// Validates the match-history pagination query: <c>Page &gt;= 1</c> and <c>1 &lt;= PageSize &lt;= 50</c>.
/// </summary>
public sealed class MatchHistoryQueryValidator : AbstractValidator<MatchHistoryQuery>
{
    public MatchHistoryQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be 1 or greater.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 50)
            .WithMessage("PageSize must be between 1 and 50.");
    }
}
