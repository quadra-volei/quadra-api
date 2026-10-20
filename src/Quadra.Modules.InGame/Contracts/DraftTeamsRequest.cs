namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Optional body of <c>POST /matches/{id}/teams/draft</c>. With no body: two teams, balanced,
/// everybody plays.
/// </summary>
/// <param name="TeamCount">2 to 4 teams.</param>
/// <param name="PerTeam">Players per team; when there are more players than slots the extras sit out.</param>
/// <param name="Mode"><c>Balanced</c> (by level, the default) or <c>Random</c>.</param>
public sealed record DraftTeamsRequest(int? TeamCount = null, int? PerTeam = null, string? Mode = null);

/// <summary>Body of <c>POST /matches/{id}/scoreboard/sets</c>: who plays the next set.</summary>
public sealed record OpenNextSetRequest(Guid TeamAId, Guid TeamBId);
