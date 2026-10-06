namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module projection of a match's finished-game result, exposed by the InGame
/// module through <see cref="IMatchResultReader"/> and consumed by the Matches module when it
/// snapshots the immutable match summary (F1.6).
/// </summary>
public sealed record MatchResult(
    Guid MatchId,
    string ScoreboardState,       // NotStarted | InProgress | Ended
    string Format,                // BestOf3 | BestOf5
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
    int MvpTotalVotes);

/// <summary>Per-set result within a <see cref="MatchResult"/>.</summary>
public sealed record MatchResultSet(int SetNumber, int TeamAPoints, int TeamBPoints, Guid? WinnerTeamId);

/// <summary>Team composition within a <see cref="MatchResult"/>.</summary>
public sealed record MatchResultTeam(Guid TeamId, string Name, IReadOnlyList<Guid> PlayerIds);
