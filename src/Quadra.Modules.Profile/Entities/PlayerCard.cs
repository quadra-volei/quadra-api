namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// A materialized snapshot of a player's FIFA-style card, stored in its own <c>player_cards</c> table.
/// One row per player, first generated when the player crosses <see cref="GenerationThreshold"/>
/// recorded matches and refreshed on every subsequent finished match. Static display fields are
/// snapshotted here; the dynamic free/premium flag is resolved live at read time and is <b>not</b>
/// stored. All state changes go through explicit methods to enforce invariants.
/// </summary>
public sealed class PlayerCard
{
    /// <summary>Number of recorded matches at which the card is first generated.</summary>
    public const int GenerationThreshold = 3;

    // Private parameterless constructor required by EF Core.
    private PlayerCard() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>users.id</c>. No DDL FK (module boundary rule). One card per user.</summary>
    public Guid UserId { get; private set; }

    /// <summary>Snapshot of <c>player_profiles.display_name</c> at (re)generation.</summary>
    public string DisplayName { get; private set; } = null!;

    public PlayerPosition? Position { get; private set; }

    /// <summary>Snapshot of the derived <c>player_profiles.level</c>.</summary>
    public PlayerLevel Level { get; private set; }

    public int MatchesPlayed { get; private set; }

    public int Wins { get; private set; }

    public int Losses { get; private set; }

    public int Draws { get; private set; }

    public int MvpsReceived { get; private set; }

    /// <summary>When the card was first generated (the 3rd-match crossing). Set once, never overwritten.</summary>
    public DateTimeOffset GeneratedAt { get; private set; }

    /// <summary>Timestamp of the last snapshot refresh.</summary>
    public DateTimeOffset RefreshedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Factory method — generates a card for a player crossing the match threshold for the first time.
    /// <see cref="GeneratedAt"/> and <see cref="RefreshedAt"/> are both set to <paramref name="generatedAt"/>.
    /// </summary>
    public static PlayerCard Generate(
        Guid userId,
        string displayName,
        PlayerPosition? position,
        PlayerLevel level,
        int matchesPlayed,
        int wins,
        int losses,
        int draws,
        int mvpsReceived,
        DateTimeOffset generatedAt)
    {
        return new PlayerCard
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            DisplayName = displayName,
            Position = position,
            Level = level,
            MatchesPlayed = matchesPlayed,
            Wins = wins,
            Losses = losses,
            Draws = draws,
            MvpsReceived = mvpsReceived,
            GeneratedAt = generatedAt,
            RefreshedAt = generatedAt,
            CreatedAt = generatedAt,
        };
    }

    /// <summary>
    /// Overwrites the snapshot display fields from freshly recomputed profile/stats values and updates
    /// <see cref="RefreshedAt"/>. <see cref="GeneratedAt"/> is preserved (set once at first generation).
    /// </summary>
    public void Refresh(
        string displayName,
        PlayerPosition? position,
        PlayerLevel level,
        int matchesPlayed,
        int wins,
        int losses,
        int draws,
        int mvpsReceived,
        DateTimeOffset refreshedAt)
    {
        DisplayName = displayName;
        Position = position;
        Level = level;
        MatchesPlayed = matchesPlayed;
        Wins = wins;
        Losses = losses;
        Draws = draws;
        MvpsReceived = mvpsReceived;
        RefreshedAt = refreshedAt;
    }
}
