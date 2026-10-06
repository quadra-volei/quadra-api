using FluentValidation;
using Quadra.Modules.Geo.Contracts;

namespace Quadra.Modules.Geo.Validation;

/// <summary>
/// Validates <see cref="NearbyMatchesQuery"/> query parameters for <c>GET /api/v1/matches/nearby</c>.
/// </summary>
public sealed class NearbyMatchesQueryValidator : AbstractValidator<NearbyMatchesQuery>
{
    public NearbyMatchesQueryValidator()
    {
        RuleFor(x => x.Lat)
            .InclusiveBetween(-90, 90)
            .WithMessage("Lat must be between -90 and 90.");

        RuleFor(x => x.Lon)
            .InclusiveBetween(-180, 180)
            .WithMessage("Lon must be between -180 and 180.");

        RuleFor(x => x.RadiusKm)
            .GreaterThan(0)
            .LessThanOrEqualTo(50)
            .WithMessage("RadiusKm must be greater than 0 and at most 50.");
    }
}
