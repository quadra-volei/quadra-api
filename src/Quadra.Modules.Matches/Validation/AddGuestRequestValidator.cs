using FluentValidation;
using Quadra.Modules.Matches.Contracts;

namespace Quadra.Modules.Matches.Validation;

/// <summary>FluentValidation rules for <see cref="AddGuestRequest"/>.</summary>
public sealed class AddGuestRequestValidator : AbstractValidator<AddGuestRequest>
{
    public const int MaxNameLength = 80;

    private static readonly HashSet<string> ValidPositions =
        new(StringComparer.Ordinal) { "LEV", "PON", "OPO", "CEN", "LIB", "COR" };

    public AddGuestRequestValidator()
    {
        RuleFor(x => x.Name)
            .Must(name => (name ?? string.Empty).Trim().Length is >= 1 and <= MaxNameLength)
            .WithMessage($"Name must be between 1 and {MaxNameLength} characters (after trimming).");

        RuleFor(x => x.Position)
            .Must(position => ValidPositions.Contains(position!))
            .When(x => x.Position is not null)
            .WithMessage("Position must be one of: LEV, PON, OPO, CEN, LIB, COR.");
    }
}
