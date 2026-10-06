namespace Quadra.Modules.InGame.Entities;

/// <summary>
/// Represents a single set within a <see cref="Scoreboard"/>.
/// All state changes go through explicit methods to enforce invariants.
/// </summary>
public sealed class ScoreboardSet
{
    // Private parameterless constructor required by EF Core.
    private ScoreboardSet() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>scoreboards.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid ScoreboardId { get; private set; }

    /// <summary>
    /// Denormalized app-layer FK to <c>matches.id</c> for efficient per-match queries.
    /// No DDL FK (module boundary rule).
    /// </summary>
    public Guid MatchId { get; private set; }

    /// <summary>Set number within the match (<c>1..5</c>).</summary>
    public int SetNumber { get; private set; }

    public int TeamAPoints { get; private set; }
    public int TeamBPoints { get; private set; }

    public SetStatus Status { get; private set; }

    /// <summary>Deciding set (target 15 instead of 25).</summary>
    public bool IsDecidingSet { get; private set; }

    /// <summary>Winner of the set; <c>null</c> while in progress or when the set is abandoned.</summary>
    public Guid? WinnerTeamId { get; private set; }

    /// <summary>The two teams that play this set. Null on sets recorded before teams could rotate.</summary>
    public Guid? TeamAId { get; private set; }

    public Guid? TeamBId { get; private set; }

    /// <summary>Who scored the last point, while it can still be undone.</summary>
    public Guid? LastPointTeamId { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? FinishedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Factory method — the only way to create a new <see cref="ScoreboardSet"/> instance.
    /// </summary>
    public static ScoreboardSet Create(
        Guid scoreboardId,
        Guid matchId,
        int setNumber,
        bool isDecidingSet,
        DateTimeOffset now,
        Guid? teamAId = null,
        Guid? teamBId = null)
    {
        return new ScoreboardSet
        {
            Id = Guid.NewGuid(),
            ScoreboardId = scoreboardId,
            MatchId = matchId,
            SetNumber = setNumber,
            TeamAPoints = 0,
            TeamBPoints = 0,
            Status = SetStatus.InProgress,
            IsDecidingSet = isDecidingSet,
            WinnerTeamId = null,
            TeamAId = teamAId,
            TeamBId = teamBId,
            StartedAt = now,
            FinishedAt = null,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }

    /// <summary>
    /// Adds a single point to the scoring team. The caller guarantees
    /// <paramref name="scoringTeamId"/> is either <paramref name="teamAId"/> or <paramref name="teamBId"/>.
    /// </summary>
    public void AddPoint(Guid scoringTeamId, Guid teamAId, Guid teamBId)
    {
        if (scoringTeamId == teamAId)
        {
            TeamAPoints++;
        }
        else if (scoringTeamId == teamBId)
        {
            TeamBPoints++;
        }
        else
        {
            return;
        }

        LastPointTeamId = scoringTeamId;
    }

    /// <summary>Takes back the last point. False when there is none to take back.</summary>
    public bool UndoLastPoint(Guid teamAId, Guid teamBId)
    {
        if (LastPointTeamId == teamAId && TeamAPoints > 0)
        {
            TeamAPoints--;
        }
        else if (LastPointTeamId == teamBId && TeamBPoints > 0)
        {
            TeamBPoints--;
        }
        else
        {
            return false;
        }

        LastPointTeamId = null;
        return true;
    }

    /// <summary>
    /// Marks the set as finished. <paramref name="winnerTeamId"/> is <c>null</c> for an abandoned set
    /// (forced early game end).
    /// </summary>
    public void Finish(Guid? winnerTeamId, DateTimeOffset now)
    {
        Status = SetStatus.Finished;
        LastPointTeamId = null;
        WinnerTeamId = winnerTeamId;
        FinishedAt = now;
        UpdatedAt = now;
    }
}
