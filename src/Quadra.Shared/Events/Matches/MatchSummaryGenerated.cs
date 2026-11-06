namespace Quadra.Shared.Events.Matches;

/// <summary>
/// Integration event published by the Matches module after the immutable match summary is
/// persisted (F1.6). This is the canonical "match fully completed" signal.
/// Handled in-process by: <c>MatchSummaryGeneratedProfileConsumer</c> (stats and match history),
/// <c>MatchSummaryGeneratedRankingConsumer</c> (points), <c>ActivityConsumers</c> and
/// <c>NotificationConsumers</c> ("summary ready").
/// Handlers must be idempotent.
/// </summary>
public sealed record MatchSummaryGenerated(
    Guid MatchId,
    Guid WinnerTeamId,          // Guid.Empty when the game ended level (no winner)
    Guid? MvpPlayerId,
    int DurationSeconds,
    IReadOnlyList<Guid> ParticipantPlayerIds,
    DateTimeOffset GeneratedAt,
    DateTimeOffset OccurredAt);
