using FluentValidation;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Validation;

/// <summary>
/// Validates <see cref="UpdateProfileRequest"/>. The photo-object-key ownership check reads the
/// caller id from <see cref="ValidationContext{T}.RootContextData"/> under <see cref="CallerIdKey"/>
/// (set by the controller), preventing a caller from referencing another user's object.
/// State-dependent rules (what onboarding requires, handle uniqueness) live in the handler.
/// </summary>
public sealed class UpdateProfileRequestValidator : AbstractValidator<UpdateProfileRequest>
{
    /// <summary>Root-context-data key under which the controller supplies the caller id (a <see cref="Guid"/>).</summary>
    public const string CallerIdKey = "CallerId";

    public const int MaxNameLength = 40;

    /// <summary>Oldest accepted birth year — guards against typos, not a product rule.</summary>
    public const int MinBirthYear = 1900;

    /// <summary>The levels a player may declare. Elite is never self-declared.</summary>
    public static readonly IReadOnlySet<string> DeclarableLevels = new HashSet<string>(StringComparer.Ordinal)
    {
        nameof(PlayerLevel.Beginner),
        nameof(PlayerLevel.Intermediate),
        nameof(PlayerLevel.Advanced),
    };

    private static readonly IReadOnlySet<string> Positions =
        Enum.GetNames<PlayerPosition>().ToHashSet(StringComparer.Ordinal);

    private static readonly IReadOnlySet<string> Modalities =
        Enum.GetNames<PlayerModality>().ToHashSet(StringComparer.Ordinal);

    public UpdateProfileRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.FirstName)
            .Must(BeAName)
            .WithMessage($"FirstName must be between 1 and {MaxNameLength} characters (after trimming).");

        RuleFor(x => x.LastName)
            .Must(BeAName)
            .WithMessage($"LastName must be between 1 and {MaxNameLength} characters (after trimming).");

        RuleFor(x => x.Handle)
            .Must(ProfileHandle.IsValid)
            .WithMessage(ProfileHandle.RuleMessage);

        RuleFor(x => x.BirthDate)
            .Must(date => date.Year >= MinBirthYear
                && date < DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .WithMessage("BirthDate must be a past date.");

        RuleFor(x => x.Position)
            .Must(position => position is not null && Positions.Contains(position))
            .WithMessage($"Position must be one of: {string.Join(", ", Enum.GetNames<PlayerPosition>())}.");

        RuleFor(x => x.Modality)
            .Must(modality => Modalities.Contains(modality!))
            .When(x => x.Modality is not null)
            .WithMessage($"Modality must be one of: {string.Join(", ", Enum.GetNames<PlayerModality>())}.");

        RuleFor(x => x.Level)
            .Must(level => DeclarableLevels.Contains(level!))
            .When(x => x.Level is not null)
            .WithMessage("Level must be one of: Beginner, Intermediate, Advanced.");

        RuleFor(x => x.PhotoObjectKey)
            .Custom(ValidatePhotoObjectKey);
    }

    private static bool BeAName(string? value)
    {
        var trimmed = (value ?? string.Empty).Trim();
        return trimmed.Length is >= 1 and <= MaxNameLength;
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
