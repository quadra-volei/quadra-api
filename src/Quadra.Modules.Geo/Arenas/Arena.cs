using NetTopologySuite.Geometries;
using Quadra.Modules.Geo.Venues;

namespace Quadra.Modules.Geo.Arenas;

/// <summary>
/// A place to play, as we curate it: one arena for however many Google entries it has
/// (<see cref="ArenaPlace"/>). Only <see cref="ArenaStatuses.Active"/> arenas, approved by a person,
/// go on the map. Holds nothing copied from Google: no name, no address, and the point is ours
/// (never <see cref="Venue.Location"/>, which Google lets us keep for 30 days only).
/// </summary>
public sealed class Arena
{
    private Arena() { }

    public Guid Id { get; private set; }

    /// <summary>PostGIS geography point (SRID 4326). X = Longitude, Y = Latitude.</summary>
    public Point Location { get; private set; } = null!;

    /// <summary>One of <see cref="VenueKinds"/>.</summary>
    public string Kind { get; private set; } = null!;

    /// <summary>One of <see cref="ArenaStatuses"/>.</summary>
    public string Status { get; private set; } = null!;

    /// <summary>The arena this one was merged into; set only when <see cref="Status"/> is Merged.</summary>
    public Guid? MergedIntoId { get; private set; }

    /// <summary>The admin who last approved, deactivated or merged it. Null while a draft.</summary>
    public Guid? ReviewedBy { get; private set; }

    public DateTimeOffset? ReviewedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>A new arena, in Draft: not on the map until <see cref="Approve"/>.</summary>
    public static Arena Create(double latitude, double longitude, string kind, DateTimeOffset now)
    {
        if (kind is not (VenueKinds.Court or VenueKinds.Sand or VenueKinds.Park))
        {
            throw new ArgumentException($"Unknown arena kind '{kind}'.", nameof(kind));
        }

        return new Arena
        {
            Id = Guid.NewGuid(),
            Location = new Point(longitude, latitude) { SRID = 4326 },
            Kind = kind,
            Status = ArenaStatuses.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Draft or Inactive → Active: the arena shows on the map.</summary>
    public void Approve(Guid reviewerId, DateTimeOffset now)
    {
        Require(Status is ArenaStatuses.Draft or ArenaStatuses.Inactive, "approve");
        Review(ArenaStatuses.Active, reviewerId, now);
    }

    /// <summary>Draft or Active → Inactive: off the map, kept so its games and places stay linked.</summary>
    public void Deactivate(Guid reviewerId, DateTimeOffset now)
    {
        Require(Status is ArenaStatuses.Draft or ArenaStatuses.Active, "deactivate");
        Review(ArenaStatuses.Inactive, reviewerId, now);
    }

    /// <summary>
    /// The same place as <paramref name="target"/>, which stays: this one becomes Merged and points
    /// at it. Moving the places and games over is the caller's job.
    /// </summary>
    public void MergeInto(Arena target, Guid reviewerId, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(target);
        Require(Status is not ArenaStatuses.Merged, "merge");
        if (target.Id == Id || target.Status is not ArenaStatuses.Active)
        {
            throw new InvalidOperationException("An arena can only be merged into another, active arena.");
        }

        Review(ArenaStatuses.Merged, reviewerId, now);
        MergedIntoId = target.Id;
    }

    private void Review(string status, Guid reviewerId, DateTimeOffset now)
    {
        Status = status;
        ReviewedBy = reviewerId;
        ReviewedAt = now;
        UpdatedAt = now;
    }

    private void Require(bool allowed, string action)
    {
        if (!allowed)
        {
            throw new InvalidOperationException($"Cannot {action} an arena with status '{Status}'.");
        }
    }
}

/// <summary>
/// What a person decided about one Google place id: it belongs to an arena, or it is not a place to
/// play and discovery should not offer it again. A place id with no row is still pending. The row
/// outlives the <see cref="Venue"/> cache: Google lets us keep place ids, so there is no link to
/// <c>venues</c>.
/// </summary>
public sealed class ArenaPlace
{
    private ArenaPlace() { }

    /// <summary>Google Places id; the key, so a place belongs to one arena at most.</summary>
    public string PlaceId { get; private set; } = null!;

    /// <summary>Set when <see cref="Status"/> is Linked, null when Rejected.</summary>
    public Guid? ArenaId { get; private set; }

    /// <summary>One of <see cref="ArenaPlaceStatuses"/>.</summary>
    public string Status { get; private set; } = null!;

    /// <summary>The arena's main entry (one per arena); the others are side entrances or duplicates.</summary>
    public bool IsPrimary { get; private set; }

    public Guid ReviewedBy { get; private set; }

    public DateTimeOffset ReviewedAt { get; private set; }

    public static ArenaPlace Link(string placeId, Guid arenaId, bool isPrimary, Guid reviewerId, DateTimeOffset now) =>
        new()
        {
            PlaceId = placeId,
            ArenaId = arenaId,
            Status = ArenaPlaceStatuses.Linked,
            IsPrimary = isPrimary,
            ReviewedBy = reviewerId,
            ReviewedAt = now,
        };

    public static ArenaPlace Reject(string placeId, Guid reviewerId, DateTimeOffset now) =>
        new()
        {
            PlaceId = placeId,
            Status = ArenaPlaceStatuses.Rejected,
            ReviewedBy = reviewerId,
            ReviewedAt = now,
        };
}

/// <summary>Where an arena is in curation. Only Active ones are shown.</summary>
public static class ArenaStatuses
{
    public const string Draft = "Draft";
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Merged = "Merged";
}

/// <summary>What was decided about a Google place id.</summary>
public static class ArenaPlaceStatuses
{
    public const string Linked = "Linked";
    public const string Rejected = "Rejected";
}
