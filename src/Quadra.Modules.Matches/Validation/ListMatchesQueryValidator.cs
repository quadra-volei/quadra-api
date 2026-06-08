using FluentValidation;
using Quadra.Modules.Matches.Contracts;

namespace Quadra.Modules.Matches.Validation;

/// <summary>
/// Validates <see cref="ListMatchesQuery"/> query parameters.
/// </summary>
public sealed class ListMatchesQueryValidator : AbstractValidator<ListMatchesQuery>
{
    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Draft", "Open", "Closed", "InProgress", "Ended", "Cancelled",
        };

    public ListMatchesQueryValidator()
    {
        RuleFor(x => x.Page)
            .GreaterThanOrEqualTo(1)
            .WithMessage("Page must be >= 1.");

        RuleFor(x => x.PageSize)
            .InclusiveBetween(1, 100)
            .WithMessage("PageSize must be between 1 and 100.");

        RuleFor(x => x.Status)
            .Must(s => s is null || ValidStatuses.Contains(s))
            .WithMessage("Status must be one of: Draft, Open, Closed, InProgress, Ended, Cancelled.");
    }
}
