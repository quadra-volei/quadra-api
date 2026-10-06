namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// A match. The invite code of a private match is never part of this payload — it is only
/// returned to the organizer by the detail endpoint.
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
    DateTimeOffset UpdatedAt,
    string? Format = null,
    string? Level = null,
    int? DurationMinutes = null,
    string Visibility = "Open",
    string? InviteMode = null,
    decimal? PriceMonthly = null,
    IReadOnlyList<int>? RecurrenceDays = null);
