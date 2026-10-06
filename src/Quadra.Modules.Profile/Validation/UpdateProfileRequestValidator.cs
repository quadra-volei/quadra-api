using FluentValidation;
using Quadra.Modules.Profile.Contracts;

namespace Quadra.Modules.Profile.Validation;

/// <summary>
/// Validates <see cref="UpdateProfileRequest"/>. The photo-object-key ownership check reads the
/// caller id from <see cref="ValidationContext{T}.RootContextData"/> under <see cref="CallerIdKey"/>
/// (set by the controller), preventing a caller from referencing another user's object.
/// </summary>
public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    /// <summary>Root-context-data key under which the controller supplies the caller id (a <see cref="Guid"/>).</summary>
    public const string CallerIdKey = "CallerId";

    private static readonly HashSet<string> ValidPositions =
        new(StringComparer.Ordinal) { "Setter", "OutsideHitter", "Opposite", "MiddleBlocker", "Libero" };

    private const string PositionMessage =
        "must be one of: Setter, OutsideHitter, Opposite, MiddleBlocker, Libero.";

    public UpdateProfileRequestValidator()
    {
        RuleFor(x => x.DisplayName)
            .NotNull()
            .Must(d => { var trimmed = (d ?? string.Empty).Trim(); return trimmed.Length is >= 2 and <= 80; })
            .WithMessage("DisplayName must be between 2 and 80 characters (after trimming).");

        RuleFor(x => x.PrimaryPosition)
            .Must(BeAValidPosition)
            .When(x => x.PrimaryPosition is not null)
            .WithMessage($"PrimaryPosition {PositionMessage}");

        RuleFor(x => x.SecondaryPosition)
            .Must(BeAValidPosition)
            .When(x => x.SecondaryPosition is not null)
            .WithMessage($"SecondaryPosition {PositionMessage}");

        RuleFor(x => x.SecondaryPosition)
            .Must((request, secondary) => request.PrimaryPosition is not null)
            .When(x => x.SecondaryPosition is not null)
            .WithMessage("SecondaryPosition may only be set when PrimaryPosition is set.");

        RuleFor(x => x.SecondaryPosition)
            .Must((request, secondary) => !string.Equals(secondary, request.PrimaryPosition, StringComparison.Ordinal))
            .When(x => x.SecondaryPosition is not null && x.PrimaryPosition is not null)
            .WithMessage("SecondaryPosition must differ from PrimaryPosition.");

        RuleFor(x => x.PhotoObjectKey)
            .Custom(ValidatePhotoObjectKey);
    }

    private static bool BeAValidPosition(string? value)
    {
        return value is not null && ValidPositions.Contains(value);
    }

    private static void ValidatePhotoObjectKey(string? key, ValidationContext<UpdateProfileRequest> context)
    {
        if (string.IsNullOrEmpty(key))
        {
            return;
        }

        if (key.Length > 512)
        {
            context.AddFailure(
                nameof(UpdateProfileRequest.PhotoObjectKey),
                "PhotoObjectKey must be 512 characters or fewer.");
        }

        if (context.RootContextData.TryGetValue(CallerIdKey, out var raw) && raw is Guid callerId)
        {
            var expectedPrefix = $"profiles/{callerId}/";
            if (!key.StartsWith(expectedPrefix, StringComparison.Ordinal))
            {
                context.AddFailure(
                    nameof(UpdateProfileRequest.PhotoObjectKey),
                    "PhotoObjectKey must reference an object owned by the caller.");
            }
        }
    }
}
