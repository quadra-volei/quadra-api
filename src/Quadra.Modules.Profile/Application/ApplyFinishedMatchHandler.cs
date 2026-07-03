using Microsoft.Extensions.Logging;
using Quadra.Modules.Profile.Abstractions;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Applies one finished-match participation: upserts the player's match-history row, then
/// <b>recomputes</b> their stats and level from all history rows (recompute-not-increment).
/// Idempotent under at-least-once delivery — a redelivered event reproduces the same result.
/// </summary>
public sealed class ApplyFinishedMatchHandler : IPlayerStatsWriter
{
    private readonly IPlayerProfileRepository _profiles;
    private readonly IPlayerMatchHistoryRepository _history;
    private readonly IPlayerCardRepository _cards;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ApplyFinishedMatchHandler> _logger;

    public ApplyFinishedMatchHandler(
        IPlayerProfileRepository profiles,
        IPlayerMatchHistoryRepository history,
        IPlayerCardRepository cards,
        TimeProvider timeProvider,
        ILogger<ApplyFinishedMatchHandler> logger)
    {
        _profiles = profiles;
        _history = history;
        _cards = cards;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ApplyFinishedMatchAsync(
        FinishedMatchParticipation participation,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(participation);

        var now = _timeProvider.GetUtcNow();
        var outcome = ParseOutcome(participation.Outcome);

        // 1. Upsert the history row (idempotent on user_id + match_id).
        var entry = PlayerMatchHistoryEntry.Create(
            participation.PlayerId,
            participation.MatchId,
            participation.MatchName,
            participation.MatchDateTime,
            participation.TeamId,
            outcome,
            participation.WasMvp,
            participation.DurationSeconds,
            participation.FinishedAt,
            now);

        await _history.UpsertAsync(entry, cancellationToken);

        // 2. Recompute stats + level from all history rows.
        var counts = await _history.GetOutcomeCountsAsync(participation.PlayerId, cancellationToken);
        var found = await _profiles.FindByUserIdAsync(participation.PlayerId, cancellationToken);

        PlayerProfile profile;
        PlayerStats stats;

        if (found is null)
        {
            // Defensive: profile should already be provisioned; bootstrap it if not.
            profile = PlayerProfile.Provision(participation.PlayerId, now);
            stats = PlayerStats.Empty(participation.PlayerId, now);
            stats.Recompute(counts.Wins, counts.Losses, counts.Draws, counts.MvpsReceived, now);
            profile.SetLevel(PlayerLevelCalculator.Calculate(stats), now);
            await _profiles.AddAsync(profile, stats, cancellationToken);
        }
        else
        {
            found.Stats.Recompute(counts.Wins, counts.Losses, counts.Draws, counts.MvpsReceived, now);
            found.Profile.SetLevel(PlayerLevelCalculator.Calculate(found.Stats), now);
            await _profiles.UpdateAsync(found.Profile, cancellationToken);
            profile = found.Profile;
            stats = found.Stats;
        }

        // 3. (Re)generate the player card once the player has crossed the match threshold (F2.2).
        //    Below the threshold no card row is written. Idempotent: recompute-not-increment stats
        //    reproduce the identical snapshot on redelivery.
        if (stats.MatchesPlayed >= PlayerCard.GenerationThreshold)
        {
            await UpsertCardAsync(participation.PlayerId, profile, stats, now, cancellationToken);
        }

        _logger.LogInformation(
            "Applied finished match {MatchId} for {UserId} with outcome {OutcomeCategory}.",
            participation.MatchId,
            participation.PlayerId,
            outcome);
    }

    /// <summary>
    /// Upserts the player-card snapshot: <see cref="PlayerCard.Generate"/> on the first threshold
    /// crossing, <see cref="PlayerCard.Refresh"/> thereafter (which preserves <c>generated_at</c>).
    /// </summary>
    private async Task UpsertCardAsync(
        Guid userId,
        PlayerProfile profile,
        PlayerStats stats,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = (await _cards.FindByUserIdAsync(userId, cancellationToken)).Card;

        PlayerCard card;
        if (existing is null)
        {
            card = PlayerCard.Generate(
                userId,
                profile.DisplayName,
                profile.PrimaryPosition,
                profile.SecondaryPosition,
                profile.Level,
                stats.MatchesPlayed,
                stats.Wins,
                stats.Losses,
                stats.Draws,
                stats.MvpsReceived,
                now);
        }
        else
        {
            existing.Refresh(
                profile.DisplayName,
                profile.PrimaryPosition,
                profile.SecondaryPosition,
                profile.Level,
                stats.MatchesPlayed,
                stats.Wins,
                stats.Losses,
                stats.Draws,
                stats.MvpsReceived,
                now);
            card = existing;
        }

        await _cards.UpsertAsync(card, cancellationToken);
    }

    private static MatchOutcome ParseOutcome(string value)
    {
        if (Enum.TryParse<MatchOutcome>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown match outcome.");
    }
}
