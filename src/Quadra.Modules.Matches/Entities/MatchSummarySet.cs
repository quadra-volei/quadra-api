namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Immutable snapshot of a single set within a <see cref="MatchSummary"/>.
/// <c>match_id</c> is denormalized for efficient per-match queries.
/// </summary>
public sealed class MatchSummarySet
{
    // Private parameterless constructor required by EF Core.
    private MatchSummarySet() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>match_summaries.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid MatchSummaryId { get; private set; }

    /// <summary>Denormalized app-layer FK to <c>matches.id</c> for per-match queries.</summary>
    public Guid MatchId { get; private set; }

    /// <summary>Set number within the match (<c>1..5</c>).</summary>
    public int SetNumber { get; private set; }

    public int TeamAPoints { get; private set; }
    public int TeamBPoints { get; private set; }

    /// <summary><c>null</c> for an abandoned set on a forced end.</summary>
    public Guid? WinnerTeamId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Factory method — the only way to create a <see cref="MatchSummarySet"/>.
    /// Used exclusively by <see cref="MatchSummary.Generate"/>.
    /// </summary>
    internal static MatchSummarySet Create(
        Guid matchSummaryId,
        Guid matchId,
        int setNumber,
        int teamAPoints,
        int teamBPoints,
        Guid? winnerTeamId,
        DateTimeOffset now)
    {
        return new MatchSummarySet
        {
            Id = Guid.NewGuid(),
            MatchSummaryId = matchSummaryId,
            MatchId = matchId,
            SetNumber = setNumber,
            TeamAPoints = teamAPoints,
            TeamBPoints = teamBPoints,
            WinnerTeamId = winnerTeamId,
            CreatedAt = now,
        };
    }
}
