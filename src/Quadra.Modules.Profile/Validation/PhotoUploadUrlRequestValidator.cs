using FluentValidation;
using Quadra.Modules.Profile.Contracts;

namespace Quadra.Modules.Profile.Validation;

/// <summary>
/// Validates <see cref="PhotoUploadUrlRequest"/>: <c>ContentType</c> must be a supported image type.
/// </summary>
public sealed class PhotoUploadUrlRequestValidator : AbstractValidator<PhotoUploadUrlRequest>
{
    private static readonly HashSet<string> ValidContentTypes =
        new(StringComparer.OrdinalIgnoreCase) { "image/jpeg", "image/png", "image/webp" };

    public PhotoUploadUrlRequestValidator()
    {
        RuleFor(x => x.ContentType)
            .NotEmpty()
            .Must(ct => ct is not null && ValidContentTypes.Contains(ct))
            .WithMessage("ContentType must be one of: image/jpeg, image/png, image/webp.");
    }
}
