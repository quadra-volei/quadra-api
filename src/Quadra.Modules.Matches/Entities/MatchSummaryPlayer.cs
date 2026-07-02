namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Immutable snapshot of one player's participation (and the team they belonged to) within a
/// <see cref="MatchSummary"/>. <c>match_id</c> is denormalized for efficient per-match queries.
/// </summary>
public sealed class MatchSummaryPlayer
{
    // Private parameterless constructor required by EF Core.
    private MatchSummaryPlayer() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>match_summaries.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid MatchSummaryId { get; private set; }

    /// <summary>Denormalized app-layer FK to <c>matches.id</c> for per-match queries.</summary>
    public Guid MatchId { get; private set; }

    /// <summary>Snapshot of the team the player belonged to; app-layer FK to <c>teams.id</c>.</summary>
    public Guid TeamId { get; private set; }

    /// <summary>Snapshot of the team name (e.g. "Team A").</summary>
    public string TeamName { get; private set; } = null!;

    /// <summary>App-layer FK to <c>users.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid PlayerId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Factory method — the only way to create a <see cref="MatchSummaryPlayer"/>.
    /// Used exclusively by <see cref="MatchSummary.Generate"/>.
    /// </summary>
    internal static MatchSummaryPlayer Create(
        Guid matchSummaryId,
        Guid matchId,
        Guid teamId,
        string teamName,
        Guid playerId,
        DateTimeOffset now)
    {
        return new MatchSummaryPlayer
        {
            Id = Guid.NewGuid(),
            MatchSummaryId = matchSummaryId,
            MatchId = matchId,
            TeamId = teamId,
            TeamName = teamName,
            PlayerId = playerId,
            CreatedAt = now,
        };
    }
}
