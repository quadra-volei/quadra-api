using FluentValidation;
using Quadra.Modules.Matches.Contracts;

namespace Quadra.Modules.Matches.Validation;

/// <summary>
/// Validates <see cref="UpdateMyPresenceRequest"/> according to F1.2 business rules.
/// </summary>
public sealed class UpdateMyPresenceRequestValidator : AbstractValidator<UpdateMyPresenceRequest>
{
    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase) { "Confirmed", "Declined" };

    public UpdateMyPresenceRequestValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty()
            .Must(s => ValidStatuses.Contains(s))
            .WithMessage("Status must be 'Confirmed' or 'Declined'.");

        RuleFor(x => x.InviteCode)
            .MaximumLength(16)
            .When(x => x.InviteCode is not null);
    }
}
