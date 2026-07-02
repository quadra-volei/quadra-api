using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Entities;

/// <summary>
/// Aggregate root representing the immutable, self-contained summary of a finished match
/// (final set-by-set score, duration, MVP and team composition).
/// The record is created once via <see cref="Generate"/> and never mutated afterwards — there are
/// deliberately no state-changing methods and no <c>updated_at</c> column.
/// </summary>
public sealed class MatchSummary
{
    // Private parameterless constructor required by EF Core.
    private MatchSummary() { }

    public Guid Id { get; private set; }

    /// <summary>App-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid MatchId { get; private set; }

    /// <summary>Snapshot of the scoreboard format: <c>BestOf3</c> | <c>BestOf5</c>.</summary>
    public string Format { get; private set; } = null!;

    /// <summary>Snapshot; app-layer FK to <c>teams.id</c> (owned by InGame).</summary>
    public Guid TeamAId { get; private set; }

    /// <summary>Snapshot; app-layer FK to <c>teams.id</c> (owned by InGame).</summary>
    public Guid TeamBId { get; private set; }

    public int TeamASetsWon { get; private set; }
    public int TeamBSetsWon { get; private set; }

    /// <summary><c>null</c> when the game ended with equal sets (forced end, F1.4).</summary>
    public Guid? WinnerTeamId { get; private set; }

    /// <summary>Scoreboard start (game duration lower bound).</summary>
    public DateTimeOffset StartedAt { get; private set; }

    /// <summary>Scoreboard end (game duration upper bound).</summary>
    public DateTimeOffset EndedAt { get; private set; }

    /// <summary>Precomputed <c>ended_at - started_at</c>, in whole seconds.</summary>
    public int DurationSeconds { get; private set; }

    /// <summary>Snapshot of the decided MVP; <c>null</c> when no MVP was decided.</summary>
    public Guid? MvpPlayerId { get; private set; }

    /// <summary>Winning MVP's vote count; <c>null</c> when <see cref="MvpPlayerId"/> is <c>null</c>.</summary>
    public int? MvpVoteCount { get; private set; }

    /// <summary>Total ballots cast in the voting session (0 when no voting).</summary>
    public int MvpTotalVotes { get; private set; }

    /// <summary>Organizer who finalized the summary; app-layer FK to <c>users.id</c>.</summary>
    public Guid GeneratedBy { get; private set; }

    public DateTimeOffset GeneratedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private readonly List<MatchSummarySet> _sets = [];
    private readonly List<MatchSummaryPlayer> _players = [];

    /// <summary>
    /// Set-by-set snapshot. Populated by <see cref="Generate"/> and by the repository after loading.
    /// </summary>
    public IReadOnlyList<MatchSummarySet> Sets => _sets.AsReadOnly();

    /// <summary>
    /// Team-composition snapshot (one entry per player). Populated by <see cref="Generate"/> and by
    /// the repository after loading.
    /// </summary>
    public IReadOnlyList<MatchSummaryPlayer> Players => _players.AsReadOnly();

    /// <summary>
    /// Factory method — the only way to create a <see cref="MatchSummary"/>. Snapshots the InGame
    /// <paramref name="result"/> into a Matches-owned, self-contained aggregate together with its
    /// per-set and per-player children. The record is immutable once generated.
    /// </summary>
    public static MatchSummary Generate(
        Guid matchId,
        MatchResult result,
        Guid generatedBy,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(result);

        var durationSeconds = (int)(result.EndedAt - result.StartedAt).TotalSeconds;

        var summary = new MatchSummary
        {
            Id = Guid.NewGuid(),
            MatchId = matchId,
            Format = result.Format,
            TeamAId = result.TeamAId,
            TeamBId = result.TeamBId,
            TeamASetsWon = result.TeamASetsWon,
            TeamBSetsWon = result.TeamBSetsWon,
            WinnerTeamId = result.WinnerTeamId,
            StartedAt = result.StartedAt,
            EndedAt = result.EndedAt,
            DurationSeconds = durationSeconds,
            MvpPlayerId = result.MvpPlayerId,
            MvpVoteCount = result.MvpVoteCount,
            MvpTotalVotes = result.MvpTotalVotes,
            GeneratedBy = generatedBy,
            GeneratedAt = now,
            CreatedAt = now,
        };

        foreach (var set in result.Sets)
        {
            summary._sets.Add(MatchSummarySet.Create(
                summary.Id,
                matchId,
                set.SetNumber,
                set.TeamAPoints,
                set.TeamBPoints,
                set.WinnerTeamId,
                now));
        }

        foreach (var team in result.Teams)
        {
            foreach (var playerId in team.PlayerIds)
            {
                summary._players.Add(MatchSummaryPlayer.Create(
                    summary.Id,
                    matchId,
                    team.TeamId,
                    team.Name,
                    playerId,
                    now));
            }
        }

        return summary;
    }

    /// <summary>
    /// Appends sets loaded externally (used by the repository after a query).
    /// </summary>
    internal void AttachSets(IEnumerable<MatchSummarySet> sets)
    {
        _sets.AddRange(sets);
    }

    /// <summary>
    /// Appends players loaded externally (used by the repository after a query).
    /// </summary>
    internal void AttachPlayers(IEnumerable<MatchSummaryPlayer> players)
    {
        _players.AddRange(players);
    }
}
