namespace Quadra.Modules.Profile.Contracts;

/// <summary>Aggregated match statistics for a player.</summary>
public sealed record PlayerStatsResponse(
    int MatchesPlayed,
    int Wins,
    int Losses,
    int Draws,
    int MvpsReceived);
