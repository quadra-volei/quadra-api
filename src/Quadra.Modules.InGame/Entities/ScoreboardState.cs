namespace Quadra.Modules.InGame.Entities;

/// <summary>
/// Lifecycle state of a scoreboard (the InGame-owned game state).
/// Distinct from the Matches module's own <c>matches.status</c> lifecycle.
/// </summary>
public enum ScoreboardState
{
    NotStarted,
    InProgress,
    Ended,
}
