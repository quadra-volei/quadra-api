namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module projection of a match's finished-game result, exposed by the InGame
/// module through <see cref="IMatchResultReader"/> and consumed by the Matches module when it
/// snapshots the immutable match summary (F1.6).
/// </summary>
public sealed record MatchResult(
    Guid MatchId,
    string ScoreboardState,       // NotStarted | InProgress | Ended
    string Format,                // SingleGame | BestOf3 | BestOf5 | Free (no fixed teams)
    Guid TeamAId,
    Guid TeamBId,
    int TeamASetsWon,
    int TeamBSetsWon,
    Guid? WinnerTeamId,
    DateTimeOffset StartedAt,      // scoreboard start; meaningful when Ended
    DateTimeOffset EndedAt,        // scoreboard end; meaningful when Ended
    IReadOnlyList<MatchResultSet> Sets,
    IReadOnlyList<MatchResultTeam> Teams,
    string MvpVotingState,        // None | Open | Closed
    Guid? MvpPlayerId,
    int? MvpVoteCount,
    int MvpTotalVotes,
    IReadOnlyList<MatchResultPlayerStats>? PlayerStats = null,
    IReadOnlyDictionary<Guid, int>? MvpVotesByPlayer = null);  // votes each player received

/// <summary>Per-set result within a <see cref="MatchResult"/>, with the pair that played it (null on old sets).</summary>
public sealed record MatchResultSet(
    int SetNumber,
    int TeamAPoints,
    int TeamBPoints,
    Guid? WinnerTeamId,
    Guid? TeamAId = null,
    Guid? TeamBId = null);

/// <summary>
/// The actions recorded for one player in the match. A null action was not recorded in this
/// match (nobody in it has one), which is not the same as zero.
/// </summary>
public sealed record MatchResultPlayerStats(
    Guid PlayerId,
    int? Kills,
    int? Blocks,
    int? Aces,
    int? Digs,
    int? Assists);

/// <summary>Team composition within a <see cref="MatchResult"/>.</summary>
public sealed record MatchResultTeam(Guid TeamId, string Name, IReadOnlyList<Guid> PlayerIds);
