using FluentValidation;
using Quadra.Modules.InGame.Contracts;

namespace Quadra.Modules.InGame.Validation;

/// <summary>
/// Validates <see cref="CreateScoreboardRequest"/>: <c>Format</c>, when sent, must be
/// <c>SingleGame</c>, <c>BestOf3</c> or <c>BestOf5</c> (case-insensitive). Missing means SingleGame.
/// </summary>
public sealed class CreateScoreboardRequestValidator : AbstractValidator<CreateScoreboardRequest>
{
    private static readonly HashSet<string> ValidFormats =
        new(StringComparer.OrdinalIgnoreCase) { "SingleGame", "BestOf3", "BestOf5", "Free" };

    public CreateScoreboardRequestValidator()
    {
        RuleFor(x => x.Format)
            .Must(f => f is null || ValidFormats.Contains(f))
            .WithMessage("Format must be 'SingleGame', 'BestOf3', 'BestOf5' or 'Free'.");
    }
}
