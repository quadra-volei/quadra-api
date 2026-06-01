using FluentValidation;
using Quadra.Modules.Auth.Contracts;

namespace Quadra.Modules.Auth.Validation;

/// <summary>
/// FluentValidation rules for <see cref="RefreshRequest"/>.
/// </summary>
public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    private const int MaxRefreshTokenLength = 4096;
    private const int MaxDeviceIdLength = 128;

    public RefreshRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .MaximumLength(MaxRefreshTokenLength);

        RuleFor(x => x.DeviceId)
            .MaximumLength(MaxDeviceIdLength)
            .When(x => x.DeviceId is not null);
    }
}
