namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Request body for <c>POST /api/v1/matches</c>.
///
/// The confirmation window is given in one of two ways: explicit
/// <see cref="WindowOpensAt"/> + <see cref="WindowClosesAt"/>, or
/// <see cref="ConfirmationOpensHoursBefore"/> (the mobile form), which opens the window that
/// many hours before <see cref="DateTime"/> and closes it at <see cref="DateTime"/>.
/// </summary>
/// <param name="RegularSlots">Slots confirmable during the window; the rest are drop-in slots.</param>
/// <param name="DayOfWeek">ISO day (1–7) of a recurring match. Optional when <see cref="RecurrenceDays"/> is sent.</param>
/// <param name="Format">Players per side: <c>2X2 | 4X4 | 6X6</c>.</param>
/// <param name="Level"><c>Beginner | Intermediate | Advanced</c>.</param>
/// <param name="DurationMinutes">Planned playing time (30–600).</param>
/// <param name="Visibility"><c>Open</c> (default) or <c>Private</c>.</param>
/// <param name="InviteMode"><c>Code | Guests</c> — required for a private match.</param>
/// <param name="PriceMonthly">Monthly price for regulars; recurring matches only.</param>
/// <param name="RecurrenceDays">ISO week days (1–7) a recurring match repeats on.</param>
/// <param name="ConfirmationOpensHoursBefore">Hours before the match the confirmation window opens (1–168).</param>
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
    DateTimeOffset? WindowOpensAt = null,
    DateTimeOffset? WindowClosesAt = null,
    string? Format = null,
    string? Level = null,
    int? DurationMinutes = null,
    string? Visibility = null,
    string? InviteMode = null,
    decimal? PriceMonthly = null,
    IReadOnlyList<int>? RecurrenceDays = null,
    int? ConfirmationOpensHoursBefore = null);
