namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// A player's persistent identity: display name, positions, photo reference and derived level.
/// All state changes go through explicit methods to enforce invariants.
/// Stats live in the separate <see cref="PlayerStats"/> entity (own table).
/// </summary>
public sealed class PlayerProfile
{
    /// <summary>Placeholder display name seeded at provisioning (signup carries no name).</summary>
    public const string PlaceholderDisplayName = "Player";

    // Private parameterless constructor required by EF Core.
    private PlayerProfile() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>users.id</c>. No DDL FK (module boundary rule). One per user.</summary>
    public Guid UserId { get; private set; }

    public string DisplayName { get; private set; } = null!;

    public PlayerPosition? PrimaryPosition { get; private set; }

    public PlayerPosition? SecondaryPosition { get; private set; }

    /// <summary>S3 object key of the profile photo; <c>null</c> when no photo uploaded.</summary>
    public string? PhotoObjectKey { get; private set; }

    /// <summary>Derived skill level. Never client-set; recomputed from stats.</summary>
    public PlayerLevel Level { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Factory method — bootstraps an empty profile for a newly registered user with a placeholder
    /// name and the default <see cref="PlayerLevel.Beginner"/> level.
    /// </summary>
    public static PlayerProfile Provision(Guid userId, DateTimeOffset now)
    {
        return new PlayerProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DisplayName = PlaceholderDisplayName,
            PrimaryPosition = null,
            SecondaryPosition = null,
            PhotoObjectKey = null,
            Level = PlayerLevel.Beginner,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Applies the caller-editable fields (display name, positions, photo reference). Stats and
    /// level are untouched here.
    /// </summary>
    public void UpdateDetails(
        string displayName,
        PlayerPosition? primaryPosition,
        PlayerPosition? secondaryPosition,
        string? photoObjectKey,
        DateTimeOffset now)
    {
        DisplayName = displayName;
        PrimaryPosition = primaryPosition;
        SecondaryPosition = secondaryPosition;
        PhotoObjectKey = photoObjectKey;
        UpdatedAt = now;
    }

    /// <summary>Sets the derived level. Invoked only by the stat-recompute flow.</summary>
    public void SetLevel(PlayerLevel level, DateTimeOffset now)
    {
        Level = level;
        UpdatedAt = now;
    }
}
