namespace Quadra.Shared.Events.InGame;

/// <summary>
/// Per-set result carried within a <see cref="MatchEnded"/> integration event.
/// </summary>
public sealed record MatchEndedSetResult(
    int SetNumber,
    int TeamAPoints,
    int TeamBPoints,
    Guid? WinnerTeamId);
