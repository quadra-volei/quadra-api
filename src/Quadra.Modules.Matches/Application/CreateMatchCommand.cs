namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Immutable command carrying all validated inputs needed to create a new match.
/// </summary>
public sealed record CreateMatchCommand(
    Guid OrganizerId,
    string Name,
    string? Description,
    string Address,
    double Latitude,
    double Longitude,
    DateTimeOffset DateTime,
    int MaxPlayers,
    int RegularSlots,
    decimal? Price,
    string Type,
    string? Frequency,
    int? DayOfWeek,
    DateTimeOffset WindowOpensAt,
    DateTimeOffset WindowClosesAt);
