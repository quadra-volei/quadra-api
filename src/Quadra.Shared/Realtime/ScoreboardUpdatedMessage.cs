namespace Quadra.Shared.Realtime;

/// <summary>
/// SignalR payload sent to the <c>match:{matchId}</c> group on every scoreboard mutation
/// (start, point recorded, set finished, game ended).
/// </summary>
public sealed record ScoreboardUpdatedMessage(
    Guid MatchId,
    string State,                    // NotStarted | InProgress | Ended
    string Format,                   // SingleGame | BestOf3 | BestOf5 (games before SingleGame)
    Guid TeamAId,
    Guid TeamBId,
    int TeamASetsWon,
    int TeamBSetsWon,
    int CurrentSetNumber,
    int CurrentSetTeamAPoints,
    int CurrentSetTeamBPoints,
    Guid? WinnerTeamId,
    DateTimeOffset OccurredAt,
    int? PointsToWin = null);       // SingleGame: points that win the game
