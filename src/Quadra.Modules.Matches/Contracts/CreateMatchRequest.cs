namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/matches</c>.
/// </summary>
public sealed record CreateMatchRequest(
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
