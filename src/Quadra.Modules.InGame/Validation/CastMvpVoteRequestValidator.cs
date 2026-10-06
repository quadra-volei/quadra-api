using FluentValidation;
using Quadra.Modules.InGame.Contracts;

namespace Quadra.Modules.InGame.Validation;

/// <summary>
/// Validates <see cref="CastMvpVoteRequest"/>: <c>VotedPlayerId</c> must not be empty.
/// </summary>
public sealed class CastMvpVoteRequestValidator : AbstractValidator<CastMvpVoteRequest>
{
    public CastMvpVoteRequestValidator()
    {
        RuleFor(x => x.VotedPlayerId)
            .NotEmpty();
    }
}
