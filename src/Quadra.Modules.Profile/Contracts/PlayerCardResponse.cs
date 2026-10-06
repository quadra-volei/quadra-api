namespace Quadra.Modules.Profile.Contracts;

/// <summary>
/// The JSON payload behind a player's FIFA-style card. Static display fields are a snapshot refreshed
/// on every finished match; <see cref="IsPremium"/> is resolved live at read time.
/// </summary>
public sealed record PlayerCardResponse(
    Guid UserId,
    string DisplayName,
    string? Position,
    string Level,
    PlayerStatsResponse Stats,
    bool IsPremium,
    DateTimeOffset GeneratedAt,
    DateTimeOffset RefreshedAt);
