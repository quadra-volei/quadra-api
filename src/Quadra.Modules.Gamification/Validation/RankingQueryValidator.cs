using FluentValidation;
using Quadra.Modules.Gamification.Contracts;

namespace Quadra.Modules.Gamification.Validation;

/// <summary>Validates <c>GET /api/v1/rankings</c>: a known scope, a group for the group scope, a known position.</summary>
public sealed class RankingQueryValidator : AbstractValidator<RankingQuery>
{
    private static readonly string[] Scopes = [RankingScopes.Friends, RankingScopes.Group, RankingScopes.General];
    private static readonly string[] Positions = ["LEV", "PON", "OPO", "CEN", "LIB", "COR"];

    public RankingQueryValidator()
    {
        RuleFor(x => x.Scope).Must(s => Scopes.Contains(s)).WithMessage("Scope must be friends, group or general.");
        RuleFor(x => x.GroupId).NotNull().When(x => x.Scope == RankingScopes.Group).WithMessage("GroupId is required for the group scope.");
        RuleFor(x => x.Position).Must(p => p is null || Positions.Contains(p)).WithMessage("Unknown position.");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 50);
    }
}
