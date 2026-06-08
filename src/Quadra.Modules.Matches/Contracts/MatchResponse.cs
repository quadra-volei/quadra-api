namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Read model returned by match endpoints.
/// </summary>
public sealed record MatchResponse(
    Guid Id,
    Guid OrganizerId,
    string Name,
    string? Description,
    string Address,
    double Latitude,
    double Longitude,
    DateTimeOffset DateTime,
    int MaxPlayers,
    int RegularSlots,
    int DropInSlots,
    decimal? Price,
    string Type,
    string? Frequency,
    int? DayOfWeek,
    DateTimeOffset WindowOpensAt,
    DateTimeOffset WindowClosesAt,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
