namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Response DTO containing the full team composition for a match.
/// </summary>
public sealed record TeamsResponse(IReadOnlyList<TeamResponse> Teams);
