namespace Quadra.Modules.Profile.Abstractions;

/// <summary>
/// Profile-owned write interface that applies a finished-match participation: upserts the player's
/// match-history row and recomputes their stats and level. Invoked by the Background Worker when it
/// consumes <c>MatchSummaryGenerated</c>. Idempotent (recompute-not-increment on <c>(user, match)</c>).
/// </summary>
public interface IPlayerStatsWriter
{
    Task ApplyFinishedMatchAsync(
        FinishedMatchParticipation participation,
        CancellationToken cancellationToken);
}
