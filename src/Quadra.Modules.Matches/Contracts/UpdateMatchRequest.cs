namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Request body for <c>PUT /api/v1/matches/{id}</c>: the whole editable part of the match, as
/// the edit form holds it. Type, visibility and invite mode are not editable.
/// </summary>
/// <param name="PriceMonthly">Recurring matches only; ignored for a one-off.</param>
/// <param name="RecurrenceDays">Recurring matches only; empty or null keeps the current days.</param>
/// <param name="CoverObjectKey">
/// Null keeps the current cover, an empty string removes it, a key from
/// <c>POST /profiles/me/photo/upload-url</c> (the caller's) replaces it.
/// </param>
public sealed record UpdateMatchRequest(
    string Name,
    string? Description,
    string Address,
    double Latitude,
    double Longitude,
    DateTimeOffset DateTime,
    int MaxPlayers,
    int RegularSlots,
    decimal? Price,
    string? Format = null,
    string? Level = null,
    int? DurationMinutes = null,
    decimal? PriceMonthly = null,
    IReadOnlyList<int>? RecurrenceDays = null,
    string? CoverObjectKey = null);
