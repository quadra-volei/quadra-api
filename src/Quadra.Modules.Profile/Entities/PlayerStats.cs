namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// A player's aggregated match statistics, stored in its own <c>player_stats</c> table.
/// Values are always <b>recomputed</b> from the player's match-history rows (never incremented),
/// which keeps redelivered finished-match events idempotent.
/// </summary>
public sealed class PlayerStats
{
    // Private parameterless constructor required by EF Core.
    private PlayerStats() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>users.id</c>. No DDL FK (module boundary rule). One per user.</summary>
    public Guid UserId { get; private set; }

    public int MatchesPlayed { get; private set; }

    public int Wins { get; private set; }

    public int Losses { get; private set; }

    public int Draws { get; private set; }

    public int MvpsReceived { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Factory method — creates a zeroed stats row for a newly provisioned player.</summary>
    public static PlayerStats Empty(Guid userId, DateTimeOffset now)
    {
        return new PlayerStats
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MatchesPlayed = 0,
            Wins = 0,
            Losses = 0,
            Draws = 0,
            MvpsReceived = 0,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Overwrites all counters from freshly computed totals. <see cref="MatchesPlayed"/> is the sum
    /// of the three outcome counts (one history row per finished match).
    /// </summary>
    public void Recompute(int wins, int losses, int draws, int mvpsReceived, DateTimeOffset now)
    {
        Wins = wins;
        Losses = losses;
        Draws = draws;
        MvpsReceived = mvpsReceived;
        MatchesPlayed = wins + losses + draws;
        UpdatedAt = now;
    }
}
