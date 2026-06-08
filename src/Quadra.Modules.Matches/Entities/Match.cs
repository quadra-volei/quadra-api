using NetTopologySuite.Geometries;
using Quadra.Modules.Matches.Application;

namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Aggregate root representing a volleyball match.
/// All state changes go through explicit methods to enforce invariants.
/// </summary>
public sealed class Match
{
    // Private parameterless constructor required by EF Core.
    private Match() { }

    public Guid Id { get; private set; }
    public Guid OrganizerId { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string Address { get; private set; } = null!;

    /// <summary>
    /// PostGIS geography point (SRID 4326). X = Longitude, Y = Latitude.
    /// </summary>
    public Point Location { get; private set; } = null!;

    public DateTimeOffset DateTime { get; private set; }
    public short MaxPlayers { get; private set; }
    public short RegularSlots { get; private set; }

    /// <summary>
    /// Computed column: <c>max_players - regular_slots</c>. Getter-only — never set by application code.
    /// </summary>
    public short DropInSlots { get; private set; }

    public decimal? Price { get; private set; }
    public MatchType Type { get; private set; }
    public MatchFrequency? Frequency { get; private set; }

    /// <summary>
    /// ISO day-of-week (1 = Monday … 7 = Sunday). Only set for <see cref="MatchType.Recurring"/> matches.
    /// Note: C# <see cref="System.DayOfWeek"/> uses 0 = Sunday. Conversion is applied in <see cref="Create"/>.
    /// </summary>
    public short? DayOfWeek { get; private set; }

    public DateTimeOffset WindowOpensAt { get; private set; }
    public DateTimeOffset WindowClosesAt { get; private set; }
    public MatchStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Factory method — the only way to create a new <see cref="Match"/> instance.
    /// </summary>
    public static Match Create(
        Guid organizerId,
        string name,
        string? description,
        string address,
        double latitude,
        double longitude,
        DateTimeOffset dateTime,
        int maxPlayers,
        int regularSlots,
        decimal? price,
        MatchType type,
        MatchFrequency? frequency,
        int? dayOfWeekIso,
        DateTimeOffset windowOpensAt,
        DateTimeOffset windowClosesAt,
        DateTimeOffset now)
    {
        // PostGIS Point: X = longitude, Y = latitude, SRID 4326.
        var location = new Point(longitude, latitude) { SRID = 4326 };

        return new Match
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizerId,
            Name = name,
            Description = description,
            Address = address,
            Location = location,
            DateTime = dateTime,
            MaxPlayers = (short)maxPlayers,
            RegularSlots = (short)regularSlots,
            Price = price,
            Type = type,
            Frequency = frequency,
            DayOfWeek = dayOfWeekIso.HasValue ? (short)dayOfWeekIso.Value : null,
            WindowOpensAt = windowOpensAt,
            WindowClosesAt = windowClosesAt,
            Status = MatchStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Transitions the match to <see cref="MatchStatus.Cancelled"/>.
    /// Throws <see cref="InvalidMatchStatusTransitionException"/> if the match is
    /// <see cref="MatchStatus.InProgress"/> or <see cref="MatchStatus.Ended"/> (terminal states).
    /// </summary>
    public void Cancel(DateTimeOffset now)
    {
        if (Status is MatchStatus.InProgress or MatchStatus.Ended)
        {
            throw new InvalidMatchStatusTransitionException(
                $"Cannot cancel a match with status '{Status}'. Only matches that are not InProgress or Ended can be cancelled.");
        }

        Status = MatchStatus.Cancelled;
        UpdatedAt = now;
    }
}
