namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Request to create (prepare) a scoreboard for a match.
/// </summary>
public sealed record CreateScoreboardRequest(
    string Format); // "BestOf3" | "BestOf5"
