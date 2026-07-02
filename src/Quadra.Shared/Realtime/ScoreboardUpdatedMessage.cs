namespace Quadra.Shared.Realtime;

/// <summary>
/// SignalR payload sent to the <c>match:{matchId}</c> group on every scoreboard mutation
/// (start, point recorded, set finished, game ended).
/// </summary>
public sealed record ScoreboardUpdatedMessage(
    Guid MatchId,
    string State,                    // NotStarted | InProgress | Ended
    string Format,                   // BestOf3 | BestOf5
    Guid TeamAId,
    Guid TeamBId,
    int TeamASetsWon,
    int TeamBSetsWon,
    int CurrentSetNumber,
    int CurrentSetTeamAPoints,
    int CurrentSetTeamBPoints,
    Guid? WinnerTeamId,
    DateTimeOffset OccurredAt);
