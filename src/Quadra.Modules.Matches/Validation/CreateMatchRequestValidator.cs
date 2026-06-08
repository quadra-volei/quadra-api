using FluentValidation;
using Quadra.Modules.Matches.Contracts;

namespace Quadra.Modules.Matches.Validation;

/// <summary>
/// Validates <see cref="CreateMatchRequest"/> according to F1.1 business rules.
/// Uses <see cref="TimeProvider"/> for all future-date comparisons.
/// </summary>
public sealed class CreateMatchRequestValidator : AbstractValidator<CreateMatchRequest>
{
    private static readonly HashSet<string> ValidTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Recurring", "OneOff" };

    private static readonly HashSet<string> ValidFrequencies =
        new(StringComparer.OrdinalIgnoreCase) { "Weekly", "Biweekly", "Monthly" };

    private static readonly HashSet<string> ValidStatuses =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Draft", "Open", "Closed", "InProgress", "Ended", "Cancelled",
        };

    public CreateMatchRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(120);

        RuleFor(x => x.Description)
            .MaximumLength(2000)
            .When(x => x.Description is not null);

        RuleFor(x => x.Address)
            .NotEmpty()
            .MaximumLength(300);

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90.0, 90.0);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180.0, 180.0);

        RuleFor(x => x.DateTime)
            .Must(dt => dt > timeProvider.GetUtcNow())
            .WithMessage("DateTime must be in the future.");

        RuleFor(x => x.MaxPlayers)
            .InclusiveBetween(2, 100);

        RuleFor(x => x.RegularSlots)
            .GreaterThanOrEqualTo(0)
            .Must((request, slots) => slots <= request.MaxPlayers)
            .WithMessage("RegularSlots must not exceed MaxPlayers.");

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0m)
            .LessThanOrEqualTo(9999.99m)
            .When(x => x.Price.HasValue);

        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(t => ValidTypes.Contains(t))
            .WithMessage("Type must be 'Recurring' or 'OneOff'.");

        // When Type == "Recurring": Frequency and DayOfWeek are required.
        RuleFor(x => x.Frequency)
            .NotEmpty()
            .Must(f => f is not null && ValidFrequencies.Contains(f))
            .WithMessage("Frequency must be 'Weekly', 'Biweekly', or 'Monthly' for Recurring matches.")
            .When(x => string.Equals(x.Type, "Recurring", StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.DayOfWeek)
            .NotNull()
            .InclusiveBetween(1, 7)
            .WithMessage("DayOfWeek must be between 1 (Monday) and 7 (Sunday) for Recurring matches.")
            .When(x => string.Equals(x.Type, "Recurring", StringComparison.OrdinalIgnoreCase));

        // When Type == "OneOff": Frequency and DayOfWeek must be null.
        RuleFor(x => x.Frequency)
            .Null()
            .WithMessage("Frequency must be null for OneOff matches.")
            .When(x => string.Equals(x.Type, "OneOff", StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.DayOfWeek)
            .Null()
            .WithMessage("DayOfWeek must be null for OneOff matches.")
            .When(x => string.Equals(x.Type, "OneOff", StringComparison.OrdinalIgnoreCase));

        RuleFor(x => x.WindowOpensAt)
            .Must(w => w > timeProvider.GetUtcNow())
            .WithMessage("WindowOpensAt must be in the future.")
            .Must((request, w) => w < request.WindowClosesAt)
            .WithMessage("WindowOpensAt must be before WindowClosesAt.");

        RuleFor(x => x.WindowClosesAt)
            .Must((request, w) => w < request.DateTime)
            .WithMessage("WindowClosesAt must be before DateTime.");
    }
}
