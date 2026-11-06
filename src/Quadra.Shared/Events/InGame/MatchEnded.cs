namespace Quadra.Shared.Events.InGame;

/// <summary>
/// Integration event published by the InGame module after a scoreboard transitions to Ended,
/// whether by natural completion or a forced early end.
/// Handled in-process by: InGame <c>OpenMvpVotingOnMatchEnded</c> (opens the MVP voting).
/// Handlers must be idempotent.
/// </summary>
public sealed record MatchEnded(
    Guid MatchId,
    Guid TeamAId,
    Guid TeamBId,
    int TeamASetsWon,
    int TeamBSetsWon,
    Guid? WinnerTeamId,
    IReadOnlyList<MatchEndedSetResult> Sets,
    DateTimeOffset EndedAt,
    DateTimeOffset OccurredAt);
