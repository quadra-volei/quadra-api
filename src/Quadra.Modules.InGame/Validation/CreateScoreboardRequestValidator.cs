using FluentValidation;
using Quadra.Modules.InGame.Contracts;

namespace Quadra.Modules.InGame.Validation;

/// <summary>
/// Validates <see cref="CreateScoreboardRequest"/>: <c>Format</c> must be
/// <c>BestOf3</c> or <c>BestOf5</c> (case-insensitive).
/// </summary>
public sealed class CreateScoreboardRequestValidator : AbstractValidator<CreateScoreboardRequest>
{
    private static readonly HashSet<string> ValidFormats =
        new(StringComparer.OrdinalIgnoreCase) { "BestOf3", "BestOf5" };

    public CreateScoreboardRequestValidator()
    {
        RuleFor(x => x.Format)
            .NotEmpty()
            .Must(f => f is not null && ValidFormats.Contains(f))
            .WithMessage("Format must be 'BestOf3' or 'BestOf5'.");
    }
}
