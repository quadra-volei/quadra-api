namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Request to record a single point for one team in the current set.
/// </summary>
public sealed record RecordPointRequest(
    Guid TeamId); // must be team_a_id or team_b_id of the scoreboard
