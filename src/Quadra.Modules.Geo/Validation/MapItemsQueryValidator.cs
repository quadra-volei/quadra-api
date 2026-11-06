using FluentValidation;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Venues;

namespace Quadra.Modules.Geo.Validation;

/// <summary>Validates <see cref="MapItemsQuery"/> for <c>GET /api/v1/map/items</c>.</summary>
public sealed class MapItemsQueryValidator : AbstractValidator<MapItemsQuery>
{
    /// <summary>About 200 km across in São Paulo: wider than any map a list is useful for.</summary>
    public const double MaxSpanDegrees = 2;

    private static readonly string[] KnownKinds = [VenueKinds.Court, VenueKinds.Sand, VenueKinds.Park];

    public MapItemsQueryValidator()
    {
        RuleFor(x => x.South).InclusiveBetween(-90, 90);
        RuleFor(x => x.North).InclusiveBetween(-90, 90);
        RuleFor(x => x.West).InclusiveBetween(-180, 180);
        RuleFor(x => x.East).InclusiveBetween(-180, 180);

        RuleFor(x => x)
            .Must(q => q.South < q.North && q.West < q.East)
            .WithName("Bounds")
            .WithMessage("South must be below North and West left of East.");
        RuleFor(x => x)
            .Must(q => q.North - q.South <= MaxSpanDegrees && q.East - q.West <= MaxSpanDegrees)
            .WithName("Bounds")
            .WithMessage($"The rectangle can span at most {MaxSpanDegrees} degrees each way.");

        RuleFor(x => x.Days).InclusiveBetween(1, 30);

        RuleFor(x => x.Kinds)
            .Must(k => k!.Split(',', StringSplitOptions.TrimEntries).All(KnownKinds.Contains))
            .When(x => !string.IsNullOrWhiteSpace(x.Kinds))
            .WithMessage("Kinds must be a comma-separated list of Court, Sand and Park.");

        RuleFor(x => x.Lat).InclusiveBetween(-90, 90).When(x => x.Lat.HasValue);
        RuleFor(x => x.Lon).InclusiveBetween(-180, 180).When(x => x.Lon.HasValue);
    }

    /// <summary>The kinds asked for; null when every kind is.</summary>
    public static string[]? ParseKinds(string? kinds) =>
        string.IsNullOrWhiteSpace(kinds) ? null : kinds.Split(',', StringSplitOptions.TrimEntries);
}
