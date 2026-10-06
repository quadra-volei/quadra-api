namespace Quadra.Modules.Profile.Contracts;

/// <summary>The public player-card projection returned by the profile read endpoints.</summary>
public sealed record PlayerProfileResponse(
    Guid UserId,
    string DisplayName,
    string? PrimaryPosition,
    string? SecondaryPosition,
    string? PhotoUrl,
    string Level,
    PlayerStatsResponse Stats,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
