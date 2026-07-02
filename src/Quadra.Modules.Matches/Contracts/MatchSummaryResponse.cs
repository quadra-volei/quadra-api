namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Read model returned for a match's immutable summary (F1.6).
/// </summary>
public record MatchSummaryResponse(
    Guid Id,
    Guid MatchId,
    string Format,                                   // BestOf3 | BestOf5
    Guid TeamAId,
    Guid TeamBId,
    int TeamASetsWon,
    int TeamBSetsWon,
    Guid? WinnerTeamId,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    int DurationSeconds,
    Guid? MvpPlayerId,
    int? MvpVoteCount,
    int MvpTotalVotes,
    IReadOnlyList<MatchSummarySetResponse> Sets,     // ordered by SetNumber ascending
    IReadOnlyList<MatchSummaryTeamResponse> Teams,   // ordered by team name ascending
    DateTimeOffset GeneratedAt);
