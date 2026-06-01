using FluentValidation;
using Quadra.Modules.Auth.Contracts;

namespace Quadra.Modules.Auth.Validation;

/// <summary>
/// FluentValidation rules for <see cref="OidcLoginRequest"/> (shared by the Google and Apple
/// login endpoints).
/// </summary>
public sealed class OidcLoginRequestValidator : AbstractValidator<OidcLoginRequest>
{
    private const int MaxIdTokenLength = 8192;
    private const int MaxDeviceIdLength = 128;

    public OidcLoginRequestValidator()
    {
        RuleFor(x => x.IdToken)
            .NotEmpty()
            .MaximumLength(MaxIdTokenLength);

        RuleFor(x => x.DeviceId)
            .MaximumLength(MaxDeviceIdLength)
            .When(x => x.DeviceId is not null);
    }
}
