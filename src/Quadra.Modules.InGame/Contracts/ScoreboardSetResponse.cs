namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Response DTO representing a single set within a scoreboard.
/// </summary>
public sealed record ScoreboardSetResponse(
    Guid Id,
    int SetNumber,
    int TeamAPoints,
    int TeamBPoints,
    string Status,               // InProgress | Finished
    bool IsDecidingSet,
    Guid? WinnerTeamId,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    Guid? TeamAId = null,        // the pair that played this set
    Guid? TeamBId = null,
    bool CanUndo = false);       // the last point of this set can still be taken back
