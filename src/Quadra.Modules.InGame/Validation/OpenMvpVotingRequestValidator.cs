using FluentValidation;
using Quadra.Modules.InGame.Contracts;

namespace Quadra.Modules.InGame.Validation;

/// <summary>
/// Validates <see cref="OpenMvpVotingRequest"/>: when provided, <c>DeadlineAt</c> must be strictly
/// in the future.
/// </summary>
public sealed class OpenMvpVotingRequestValidator : AbstractValidator<OpenMvpVotingRequest>
{
    public OpenMvpVotingRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.DeadlineAt)
            .Must(deadline => deadline!.Value > timeProvider.GetUtcNow())
            .When(x => x.DeadlineAt.HasValue)
            .WithMessage("DeadlineAt must be in the future.");
    }
}
