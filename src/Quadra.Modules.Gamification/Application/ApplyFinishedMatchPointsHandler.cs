using Microsoft.Extensions.Logging;
using Quadra.Modules.Gamification.Abstractions;
using Quadra.Modules.Gamification.Entities;
using Quadra.Modules.Gamification.Persistence;

namespace Quadra.Modules.Gamification.Application;

/// <summary>
/// Applies a finished recurring match's point awards: inserts the immutable ledger rows idempotently,
/// then <b>recomputes</b> each affected player's <c>group_rankings</c> standing from the ledger
/// (recompute-not-increment) — all in one transaction. Idempotent under at-least-once delivery: a
/// redelivered <c>MatchSummaryGenerated</c> inserts no new ledger rows and reproduces the same standing.
/// </summary>
public sealed class ApplyFinishedMatchPointsHandler : IMatchPointsWriter
{
    private readonly GamificationDbContext _context;
    private readonly IPointTransactionRepository _transactions;
    private readonly IGroupRankingRepository _rankings;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ApplyFinishedMatchPointsHandler> _logger;

    public ApplyFinishedMatchPointsHandler(
        GamificationDbContext context,
        IPointTransactionRepository transactions,
        IGroupRankingRepository rankings,
        TimeProvider timeProvider,
        ILogger<ApplyFinishedMatchPointsHandler> logger)
    {
        _context = context;
        _transactions = transactions;
        _rankings = rankings;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ApplyFinishedMatchPointsAsync(
        FinishedMatchPoints points,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(points);

        var now = _timeProvider.GetUtcNow();
        var awards = PointRules.BuildAwards(points);

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // 1. Insert the Attendance/Win/Mvp ledger rows idempotently.
        foreach (var (userId, reason, awardPoints) in awards)
        {
            var ledgerRow = PointTransaction.Create(
                userId,
                points.GroupId,
                points.MatchId,
                reason,
                awardPoints,
                points.MatchDateTime,
                points.FinishedAt);

            await _transactions.AddIfAbsentAsync(ledgerRow, cancellationToken);
        }

        // 2. Recompute each affected player's standing from the immutable ledger.
        var affectedUserIds = awards.Select(a => a.UserId).Distinct();
        foreach (var userId in affectedUserIds)
        {
            var totalPoints = await _transactions.SumByGroupUserAsync(points.GroupId, userId, cancellationToken);
            var matchesCounted = await _transactions.CountAttendedMatchesAsync(points.GroupId, userId, cancellationToken);
            var latest = await _transactions.LatestAttendedMatchAsync(points.GroupId, userId, cancellationToken);

            await _rankings.UpsertAsync(
                points.GroupId,
                userId,
                totalPoints,
                matchesCounted,
                latest?.MatchId,
                latest?.MatchDateTime,
                now,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Applied finished match {MatchId} points for group {GroupId}: {AttendanceCount} attendance, "
            + "{WinCount} win, {MvpCount} mvp award(s).",
            points.MatchId,
            points.GroupId,
            awards.Count(a => a.Reason == PointReason.Attendance),
            awards.Count(a => a.Reason == PointReason.Win),
            awards.Count(a => a.Reason == PointReason.Mvp));
    }
}
