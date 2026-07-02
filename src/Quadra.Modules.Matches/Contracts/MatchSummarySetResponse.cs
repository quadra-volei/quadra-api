namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Per-set entry within a <see cref="MatchSummaryResponse"/>.
/// </summary>
public record MatchSummarySetResponse(
    int SetNumber,
    int TeamAPoints,
    int TeamBPoints,
    Guid? WinnerTeamId);
