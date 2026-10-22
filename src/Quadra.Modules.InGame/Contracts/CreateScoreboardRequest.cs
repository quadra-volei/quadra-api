namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Request to create (prepare) a scoreboard for a match.
/// </summary>
public sealed record CreateScoreboardRequest(
    string? Format = null,      // "SingleGame" (default) | "BestOf3" | "BestOf5" | "Free" (no fixed teams)
    Guid? TeamAId = null,       // the pair that plays the first set; defaults to the first two teams
    Guid? TeamBId = null);
