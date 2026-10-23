using FluentValidation;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Validation;

/// <summary>FluentValidation rules for <see cref="UpdateMatchRequest"/>, the same limits as creation.</summary>
public sealed class UpdateMatchRequestValidator : AbstractValidator<UpdateMatchRequest>
{
    private static readonly HashSet<string> ValidFormats =
        new(MatchSettings.Formats, StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> ValidLevels =
        new(Enum.GetNames<MatchLevel>(), StringComparer.OrdinalIgnoreCase);

    public UpdateMatchRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.Description).MaximumLength(2000).When(x => x.Description is not null);
        RuleFor(x => x.Address).NotEmpty().MaximumLength(300);
        RuleFor(x => x.Latitude).InclusiveBetween(-90.0, 90.0);
        RuleFor(x => x.Longitude).InclusiveBetween(-180.0, 180.0);

        RuleFor(x => x.DateTime)
            .Must(dt => dt > timeProvider.GetUtcNow())
            .WithMessage("DateTime must be in the future.");

        RuleFor(x => x.MaxPlayers).InclusiveBetween(2, 100);

        RuleFor(x => x.RegularSlots)
            .GreaterThanOrEqualTo(0)
            .Must((request, slots) => slots <= request.MaxPlayers)
            .WithMessage("RegularSlots must not exceed MaxPlayers.");

        RuleFor(x => x.Price)
            .GreaterThanOrEqualTo(0m)
            .LessThanOrEqualTo(9999.99m)
            .When(x => x.Price.HasValue);

        RuleFor(x => x.PriceMonthly)
            .GreaterThanOrEqualTo(0m)
            .LessThanOrEqualTo(99999.99m)
            .When(x => x.PriceMonthly.HasValue);

        RuleFor(x => x.RecurrenceDays)
            .Must(days => days!.All(d => d is >= 1 and <= 7) && days!.Distinct().Count() == days!.Count)
            .WithMessage("RecurrenceDays must be distinct ISO week days between 1 (Monday) and 7 (Sunday).")
            .When(x => x.RecurrenceDays is { Count: > 0 });

        RuleFor(x => x.Format)
            .Must(f => ValidFormats.Contains(f!))
            .WithMessage("Format must be one of: 2X2, 4X4, 6X6.")
            .When(x => x.Format is not null);

        RuleFor(x => x.Level)
            .Must(l => ValidLevels.Contains(l!))
            .WithMessage("Level must be one of: Beginner, Intermediate, Advanced.")
            .When(x => x.Level is not null);

        RuleFor(x => x.GamePoints)
            .InclusiveBetween(Match.MinGamePoints, Match.MaxGamePoints)
            .When(x => x.GamePoints.HasValue);

        RuleFor(x => x.DurationMinutes)
            .InclusiveBetween(30, 600)
            .When(x => x.DurationMinutes.HasValue);

        RuleFor(x => x.CoverObjectKey).MaximumLength(256);
        RuleFor(x => x.PlaceId).MaximumLength(255);
    }
}
