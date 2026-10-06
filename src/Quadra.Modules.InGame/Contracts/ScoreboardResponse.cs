namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Response DTO representing the full current scoreboard state including all sets.
/// </summary>
public sealed record ScoreboardResponse(
    Guid Id,
    Guid MatchId,
    string Format,               // BestOf3 | BestOf5
    string State,                // NotStarted | InProgress | Ended
    Guid TeamAId,
    Guid TeamBId,
    int TeamASetsWon,
    int TeamBSetsWon,
    int CurrentSetNumber,
    Guid? WinnerTeamId,
    IReadOnlyList<ScoreboardSetResponse> Sets,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
