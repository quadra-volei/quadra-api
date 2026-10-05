using FluentValidation;
using Quadra.Modules.Auth.Contracts;

namespace Quadra.Modules.Auth.Validation;

/// <summary>
/// FluentValidation rules for <see cref="LogoutRequest"/>.
/// </summary>
public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    private const int MaxRefreshTokenLength = 4096;

    public LogoutRequestValidator()
    {
        RuleFor(x => x.RefreshToken)
            .NotEmpty()
            .MaximumLength(MaxRefreshTokenLength);
    }
}
