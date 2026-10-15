namespace Quadra.Modules.Profile.Contracts;

/// <summary>Aggregated match statistics for a player.</summary>
/// <param name="TotalPoints">Accumulated points (attendance +10, win +15, MVP +25).</param>
/// <param name="LevelNumber">Level number reached with those points (starts at 1).</param>
/// <param name="NextLevelPoints">Total points needed for the next level number.</param>
public sealed record PlayerStatsResponse(
    int MatchesPlayed,
    int Wins,
    int Losses,
    int Draws,
    int MvpsReceived,
    int TotalPoints = 0,
    int LevelNumber = 1,
    int NextLevelPoints = 20);
