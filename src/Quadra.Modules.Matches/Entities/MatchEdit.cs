namespace Quadra.Modules.Matches.Entities;

/// <summary>The fields the organizer may change after creation (<see cref="Match.Update"/>).</summary>
public sealed record MatchEdit(
    string Name,
    string? Description,
    string Address,
    double Latitude,
    double Longitude,
    DateTimeOffset DateTime,
    int MaxPlayers,
    int RegularSlots,
    decimal? Price,
    string? Format,
    MatchLevel? Level,
    int? DurationMinutes,
    int? GamePoints,
    decimal? PriceMonthly,
    IReadOnlyList<int>? RecurrenceDays,
    string? CoverObjectKey);
