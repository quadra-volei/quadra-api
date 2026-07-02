using FluentValidation;
using Quadra.Modules.InGame.Contracts;

namespace Quadra.Modules.InGame.Validation;

/// <summary>
/// Validates <see cref="RecordPointRequest"/>: <c>TeamId</c> must not be empty.
/// </summary>
public sealed class RecordPointRequestValidator : AbstractValidator<RecordPointRequest>
{
    public RecordPointRequestValidator()
    {
        RuleFor(x => x.TeamId)
            .NotEmpty();
    }
}
