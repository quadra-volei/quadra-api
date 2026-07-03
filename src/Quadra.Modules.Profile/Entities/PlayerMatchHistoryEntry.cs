namespace Quadra.Modules.Profile.Entities;

/// <summary>
/// One player's snapshot of one finished match. Rows are upserted idempotently on
/// <c>(user_id, match_id)</c> — a match appears once per player. A row is a snapshot and is not
/// semantically mutated after recording (redelivery merely re-writes the same snapshot).
/// </summary>
public sealed class PlayerMatchHistoryEntry
{
    // Private parameterless constructor required by EF Core.
    private PlayerMatchHistoryEntry() { }

    public Guid Id { get; private set; }

    /// <summary>The player this row belongs to. App-layer FK to <c>users.id</c>.</summary>
    public Guid UserId { get; private set; }

    /// <summary>App-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid MatchId { get; private set; }

    public string MatchName { get; private set; } = null!;

    public DateTimeOffset MatchDateTime { get; private set; }

    /// <summary>The team the player was on (snapshot). App-layer FK to <c>teams.id</c>.</summary>
    public Guid? TeamId { get; private set; }

    public MatchOutcome Outcome { get; private set; }

    public bool WasMvp { get; private set; }

    public int? DurationSeconds { get; private set; }

    /// <summary>The originating summary's <c>GeneratedAt</c>.</summary>
    public DateTimeOffset RecordedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Factory method — the only way to create a new history entry.</summary>
    public static PlayerMatchHistoryEntry Create(
        Guid userId,
        Guid matchId,
        string matchName,
        DateTimeOffset matchDateTime,
        Guid? teamId,
        MatchOutcome outcome,
        bool wasMvp,
        int? durationSeconds,
        DateTimeOffset recordedAt,
        DateTimeOffset now)
    {
        return new PlayerMatchHistoryEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            MatchId = matchId,
            MatchName = matchName,
            MatchDateTime = matchDateTime,
            TeamId = teamId,
            Outcome = outcome,
            WasMvp = wasMvp,
            DurationSeconds = durationSeconds,
            RecordedAt = recordedAt,
            CreatedAt = now,
        };
    }

    /// <summary>
    /// Re-writes the snapshot values on an existing row (redelivered finished-match event).
    /// </summary>
    public void UpdateSnapshot(
        string matchName,
        DateTimeOffset matchDateTime,
        Guid? teamId,
        MatchOutcome outcome,
        bool wasMvp,
        int? durationSeconds,
        DateTimeOffset recordedAt)
    {
        MatchName = matchName;
        MatchDateTime = matchDateTime;
        TeamId = teamId;
        Outcome = outcome;
        WasMvp = wasMvp;
        DurationSeconds = durationSeconds;
        RecordedAt = recordedAt;
    }
}
