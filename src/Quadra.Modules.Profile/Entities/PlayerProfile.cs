namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// A player's identity. Provisioned empty when the user registers (<c>UserRegistered</c>) and
/// filled in by the app's onboarding (frontend S4); the shape follows the mobile app.
/// </summary>
public sealed class PlayerProfile
{
    /// <summary>Shown for a player who has not completed onboarding yet.</summary>
    public const string PlaceholderDisplayName = "Player";

    // Private parameterless constructor required by EF Core.
    private PlayerProfile() { }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string FirstName { get; private set; } = string.Empty;

    public string LastName { get; private set; } = string.Empty;

    /// <summary>
    /// Unique <c>@</c> identifier, stored lowercase without the <c>@</c>. Null until onboarding.
    /// </summary>
    public string? Handle { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    public PlayerPosition? Position { get; private set; }

    public PlayerModality? PreferredModality { get; private set; }

    /// <summary>
    /// The level the player declared at onboarding. Set once; it is the floor of
    /// <see cref="Level"/> and the base of the player's skill ratings.
    /// </summary>
    public PlayerLevel? DeclaredLevel { get; private set; }

    /// <summary>The current level (declared, then recalculated from recorded matches).</summary>
    public PlayerLevel Level { get; private set; }

    public string? PhotoObjectKey { get; private set; }

    /// <summary>
    /// "Mostrar minhas atividades para quem me segue": whether the players who follow this one see
    /// their activities (started, finished, MVP, level up). On by default.
    /// </summary>
    public bool ShareActivity { get; private set; } = true;

    /// <summary>The card's finish ("Sua carta"). Cosmetic only.</summary>
    public CardFinish CardFinish { get; private set; } = CardFinish.Prisma;

    /// <summary>Identity title code on the card; null for the app's default.</summary>
    public string? CardTitle { get; private set; }

    /// <summary>A photo just for the card; null means the card shows <see cref="PhotoObjectKey"/>.</summary>
    public string? CardPhotoObjectKey { get; private set; }

    /// <summary>When onboarding was completed; null while the profile is still the empty shell.</summary>
    public DateTimeOffset? OnboardingCompletedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public bool IsOnboardingCompleted => OnboardingCompletedAt is not null;

    /// <summary>"First Last", or the placeholder while the player has no name yet.</summary>
    public string DisplayName
    {
        get
        {
            var fullName = $"{FirstName} {LastName}".Trim();
            return fullName.Length == 0 ? PlaceholderDisplayName : fullName;
        }
    }

    public static PlayerProfile Provision(Guid userId, DateTimeOffset now)
    {
        return new PlayerProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Level = PlayerLevel.Beginner,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>Sets the fields the player can edit at any time (onboarding and S10).</summary>
    public void UpdateDetails(
        string firstName,
        string lastName,
        string handle,
        DateOnly birthDate,
        PlayerPosition position,
        string? photoObjectKey,
        DateTimeOffset now)
    {
        FirstName = firstName;
        LastName = lastName;
        Handle = handle;
        BirthDate = birthDate;
        Position = position;
        PhotoObjectKey = photoObjectKey;
        UpdatedAt = now;
    }

    public void SetPreferredModality(PlayerModality modality, DateTimeOffset now)
    {
        PreferredModality = modality;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records the self-declared level and marks onboarding complete. The declared level is
    /// permanent: a second call is rejected.
    /// </summary>
    public void CompleteOnboarding(PlayerLevel declaredLevel, DateTimeOffset now)
    {
        if (IsOnboardingCompleted)
        {
            throw new InvalidOperationException("Onboarding was already completed for this profile.");
        }

        if (declaredLevel == PlayerLevel.Elite)
        {
            throw new ArgumentOutOfRangeException(nameof(declaredLevel), declaredLevel, "Elite cannot be self-declared.");
        }

        DeclaredLevel = declaredLevel;
        OnboardingCompletedAt = now;
        UpdatedAt = now;
    }

    public void SetLevel(PlayerLevel level, DateTimeOffset now)
    {
        Level = level;
        UpdatedAt = now;
    }

    public void SetShareActivity(bool share, DateTimeOffset now)
    {
        ShareActivity = share;
        UpdatedAt = now;
    }

    public void SetCardStyle(CardFinish finish, string? title, string? photoObjectKey, DateTimeOffset now)
    {
        CardFinish = finish;
        CardTitle = title;
        CardPhotoObjectKey = photoObjectKey;
        UpdatedAt = now;
    }
}
