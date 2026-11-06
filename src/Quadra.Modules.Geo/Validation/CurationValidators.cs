using FluentValidation;
using Quadra.Modules.Geo.Arenas;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Venues;

namespace Quadra.Modules.Geo.Validation;

/// <summary>Validates <see cref="CurationDecisionRequest"/>: what each action needs.</summary>
public sealed class CurationDecisionRequestValidator : AbstractValidator<CurationDecisionRequest>
{
    private static readonly string[] Actions = [CurationActions.Approve, CurationActions.Reject, CurationActions.Link];
    private static readonly string[] Kinds = [VenueKinds.Court, VenueKinds.Sand, VenueKinds.Park];

    public CurationDecisionRequestValidator()
    {
        RuleFor(x => x.PlaceId).NotEmpty().MaximumLength(255);
        RuleFor(x => x.Action).Must(Actions.Contains).WithMessage("Action must be Approve, Reject or Link.");

        When(x => x.Action == CurationActions.Approve, () =>
        {
            RuleFor(x => x.Kind).Must(k => k is not null && Kinds.Contains(k)).WithMessage("Kind must be Court, Sand or Park.");
            RuleFor(x => x.Latitude).NotNull().InclusiveBetween(-90, 90);
            RuleFor(x => x.Longitude).NotNull().InclusiveBetween(-180, 180);
        });

        When(x => x.Action == CurationActions.Link, () => RuleFor(x => x.ArenaId).NotNull());
    }
}

/// <summary>Validates <see cref="DiscoverCandidatesRequest"/>.</summary>
public sealed class DiscoverCandidatesRequestValidator : AbstractValidator<DiscoverCandidatesRequest>
{
    public DiscoverCandidatesRequestValidator()
    {
        RuleFor(x => x.Lat).InclusiveBetween(-90, 90);
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180);
        RuleFor(x => x.RadiusKm).GreaterThan(0).LessThanOrEqualTo(VenueDiscovery.MaxRadiusKm);
    }
}
