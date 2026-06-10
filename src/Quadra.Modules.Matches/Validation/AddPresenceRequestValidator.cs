using FluentValidation;
using Quadra.Modules.Matches.Contracts;

namespace Quadra.Modules.Matches.Validation;

/// <summary>
/// Validates <see cref="AddPresenceRequest"/> according to F1.2 business rules.
/// </summary>
public sealed class AddPresenceRequestValidator : AbstractValidator<AddPresenceRequest>
{
    private static readonly HashSet<string> ValidPlayerTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Regular", "DropIn" };

    public AddPresenceRequestValidator()
    {
        RuleFor(x => x.PlayerId)
            .NotEmpty();

        RuleFor(x => x.PlayerType)
            .NotEmpty()
            .Must(t => ValidPlayerTypes.Contains(t))
            .WithMessage("PlayerType must be 'Regular' or 'DropIn'.");
    }
}
