using FluentValidation;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Validation;

/// <summary>
/// FluentValidation rules for <see cref="CreateMatchRequest"/>. The confirmation window comes
/// either as explicit <c>WindowOpensAt</c>/<c>WindowClosesAt</c> or as
/// <c>ConfirmationOpensHoursBefore</c> — exactly one of the two forms.
/// </summary>
public sealed class CreateMatchRequestValidator : AbstractValidator<CreateMatchRequest>
{
    public const int MaxConfirmationHoursBefore = 168; // one week

    private static readonly HashSet<string> ValidTypes =
        new(StringComparer.OrdinalIgnoreCase) { "Recurring", "OneOff" };

    private static readonly HashSet<string> ValidFrequencies =
        new(StringComparer.OrdinalIgnoreCase) { "Weekly", "Biweekly", "Monthly" };

    private static readonly HashSet<string> ValidFormats =
        new(MatchSettings.Formats, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ValidLevels =
        new(Enum.GetNames<MatchLevel>(), StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ValidVisibilities =
        new(Enum.GetNames<MatchVisibility>(), StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ValidInviteModes =
        new(Enum.GetNames<MatchInviteMode>(), StringComparer.OrdinalIgnoreCase);

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

        // When Type == "Recurring": Frequency is required, and the day(s) of the week come from
        // DayOfWeek or RecurrenceDays.
        RuleFor(x => x.Frequency)
            .NotEmpty()
            .Must(f => f is not null && ValidFrequencies.Contains(f))
            .WithMessage("Frequency must be 'Weekly', 'Biweekly', or 'Monthly' for Recurring matches.")
            .When(IsRecurring);

        RuleFor(x => x.DayOfWeek)
            .NotNull()
            .InclusiveBetween(1, 7)
            .WithMessage("DayOfWeek must be between 1 (Monday) and 7 (Sunday) for Recurring matches.")
            .When(x => IsRecurring(x) && x.RecurrenceDays is not { Count: > 0 });

        RuleFor(x => x.DayOfWeek)
            .InclusiveBetween(1, 7)
            .WithMessage("DayOfWeek must be between 1 (Monday) and 7 (Sunday) for Recurring matches.")
            .When(x => IsRecurring(x) && x.DayOfWeek.HasValue && x.RecurrenceDays is { Count: > 0 });

        RuleFor(x => x.RecurrenceDays)
            .Must(days => days!.All(d => d is >= 1 and <= 7) && days!.Distinct().Count() == days!.Count)
            .WithMessage("RecurrenceDays must be distinct ISO week days between 1 (Monday) and 7 (Sunday).")
            .When(x => x.RecurrenceDays is { Count: > 0 });

        // When Type == "OneOff": no recurrence settings.
        RuleFor(x => x.Frequency)
            .Null()
            .WithMessage("Frequency must be null for OneOff matches.")
            .When(IsOneOff);

        RuleFor(x => x.DayOfWeek)
            .Null()
            .WithMessage("DayOfWeek must be null for OneOff matches.")
            .When(IsOneOff);

        RuleFor(x => x.RecurrenceDays)
            .Must(days => days is not { Count: > 0 })
            .WithMessage("RecurrenceDays must be empty for OneOff matches.")
            .When(IsOneOff);

        RuleFor(x => x.PriceMonthly)
            .Null()
            .WithMessage("PriceMonthly is only valid for Recurring matches.")
            .When(IsOneOff);

        RuleFor(x => x.PriceMonthly)
            .GreaterThanOrEqualTo(0m)
            .LessThanOrEqualTo(99999.99m)
            .When(x => x.PriceMonthly.HasValue);

        // Confirmation window, explicit form.
        When(x => x.ConfirmationOpensHoursBefore is null, () =>
        {
            RuleFor(x => x.WindowOpensAt)
                .NotNull()
                .WithMessage("WindowOpensAt is required when ConfirmationOpensHoursBefore is not sent.")
                .Must(w => w is null || w > timeProvider.GetUtcNow())
                .WithMessage("WindowOpensAt must be in the future.")
                .Must((request, w) => w is null || request.WindowClosesAt is null || w < request.WindowClosesAt)
                .WithMessage("WindowOpensAt must be before WindowClosesAt.");

            RuleFor(x => x.WindowClosesAt)
                .NotNull()
                .WithMessage("WindowClosesAt is required when ConfirmationOpensHoursBefore is not sent.")
                .Must((request, w) => w is null || w < request.DateTime)
                .WithMessage("WindowClosesAt must be before DateTime.");
        });

        // Confirmation window, "opens N hours before" form.
        When(x => x.ConfirmationOpensHoursBefore is not null, () =>
        {
            RuleFor(x => x.ConfirmationOpensHoursBefore)
                .InclusiveBetween(1, MaxConfirmationHoursBefore);

            RuleFor(x => x.WindowOpensAt)
                .Null()
                .WithMessage("Send either ConfirmationOpensHoursBefore or WindowOpensAt/WindowClosesAt, not both.");

            RuleFor(x => x.WindowClosesAt)
                .Null()
                .WithMessage("Send either ConfirmationOpensHoursBefore or WindowOpensAt/WindowClosesAt, not both.");
        });

        RuleFor(x => x.Format)
            .Must(f => ValidFormats.Contains(f!))
            .WithMessage("Format must be one of: 2X2, 4X4, 6X6.")
            .When(x => x.Format is not null);

        RuleFor(x => x.Level)
            .Must(l => ValidLevels.Contains(l!))
            .WithMessage("Level must be one of: Beginner, Intermediate, Advanced.")
            .When(x => x.Level is not null);

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(30, 600)
            .When(x => x.DurationMinutes.HasValue);

        RuleFor(x => x.Visibility)
            .Must(v => ValidVisibilities.Contains(v!))
            .WithMessage("Visibility must be 'Open' or 'Private'.")
            .When(x => x.Visibility is not null);

        RuleFor(x => x.InviteMode)
            .Must(m => m is not null && ValidInviteModes.Contains(m))
            .WithMessage("InviteMode must be 'Code' or 'Guests' for a Private match.")
            .When(IsPrivate);

        RuleFor(x => x.InviteMode)
            .Null()
            .WithMessage("InviteMode is only valid for a Private match.")
            .When(x => !IsPrivate(x));
    }

    private static bool IsRecurring(CreateMatchRequest request) =>
        string.Equals(request.Type, "Recurring", StringComparison.OrdinalIgnoreCase);

    private static bool IsOneOff(CreateMatchRequest request) =>
        string.Equals(request.Type, "OneOff", StringComparison.OrdinalIgnoreCase);

    private static bool IsPrivate(CreateMatchRequest request) =>
        string.Equals(request.Visibility, "Private", StringComparison.OrdinalIgnoreCase);
}
