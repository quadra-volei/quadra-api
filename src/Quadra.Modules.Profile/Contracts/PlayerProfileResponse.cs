namespace Quadra.Modules.Profile.Contracts;

/// <summary>
/// A player's profile. <see cref="OnboardingCompleted"/> is false for the empty shell created at
/// registration; the app sends the player through onboarding until it is true.
/// <see cref="BirthDate"/> and <see cref="PhotoObjectKey"/> are only returned to the profile's owner
/// (the key is what the owner sends back on <c>PUT</c> to keep the current photo).
/// </summary>
public sealed record PlayerProfileResponse(
    Guid UserId,
    string DisplayName,
    string FirstName,
    string LastName,
    string? Handle,
    DateOnly? BirthDate,
    string? Position,
    string? Modality,
    string? DeclaredLevel,
    string Level,
    string? PhotoUrl,
    string? PhotoObjectKey,
    bool OnboardingCompleted,
    PlayerSkillsResponse Skills,
    PlayerStatsResponse Stats,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Skill ratings (1–99) derived from the declared level and position; <see cref="Overall"/> is
/// their average. Not editable.
/// </summary>
public sealed record PlayerSkillsResponse(
    int Overall,
    int Ace,
    int Block,
    int Attack,
    int Defense);
