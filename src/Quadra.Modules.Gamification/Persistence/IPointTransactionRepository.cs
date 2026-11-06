using Quadra.Modules.Gamification.Entities;

namespace Quadra.Modules.Gamification.Persistence;

/// <summary>The latest match a player attended within a group.</summary>
public sealed record AttendedMatch(Guid MatchId, DateTimeOffset MatchDateTime);

/// <summary>Points of one ledger row, at the date of its match (the season pass, DECISIONS #98).</summary>
public sealed record DatedPoints(DateTimeOffset MatchDateTime, int Points);

/// <summary>
/// One player's totals over a period: points, matches, games won (Wins), MVPs, MVP votes received,
/// and the points split into attendance, wins and MVP (votes + bonus).
/// </summary>
public sealed record SeasonTotals(
    Guid UserId,
    int Points,
    int Matches,
    int Wins,
    int Mvps,
    int Votes = 0,
    int AttendancePoints = 0,
    int WinPoints = 0,
    int MvpPoints = 0);

/// <summary>Persistence contract for the immutable <see cref="PointTransaction"/> ledger.</summary>
public interface IPointTransactionRepository
{
    /// <summary>
    /// Inserts the ledger row only if no row with the same <c>(user_id, match_id, reason)</c> exists.
    /// The idempotency guard for redelivered <c>MatchSummaryGenerated</c> events — a redelivery is a no-op.
    /// </summary>
    Task AddIfAbsentAsync(PointTransaction transaction, CancellationToken cancellationToken);

    /// <summary>Sum of a player's <c>points</c> across all reasons for a group.</summary>
    Task<int> SumByGroupUserAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);

    /// <summary>Count of distinct matches the player attended (has an <c>Attendance</c> row for) in a group.</summary>
    Task<int> CountAttendedMatchesAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);

    /// <summary>The player's most recent attended match in a group, or <c>null</c> when none.</summary>
    Task<AttendedMatch?> LatestAttendedMatchAsync(Guid groupId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Each player's totals for matches played from <paramref name="since"/> until (not including)
    /// <paramref name="until"/>, optionally only in one group and / or only for some players. Feeds
    /// the season rankings.
    /// </summary>
    Task<IReadOnlyList<SeasonTotals>> GetTotalsSinceAsync(
        DateTimeOffset since,
        DateTimeOffset until,
        Guid? groupId,
        IReadOnlyCollection<Guid>? userIds,
        CancellationToken cancellationToken);

    /// <summary>The ledger rows of one player in one match (empty when the match gave them no points).</summary>
    Task<IReadOnlyList<PointTransaction>> GetMatchAwardsAsync(Guid userId, Guid matchId, CancellationToken cancellationToken);

    /// <summary>The groups the player scored in, most recent match first.</summary>
    Task<IReadOnlyList<Guid>> GetGroupIdsAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>A player's ledger rows of matches played from <paramref name="since"/> until (not including) <paramref name="until"/>.</summary>
    Task<IReadOnlyList<DatedPoints>> GetPointsAsync(
        Guid userId,
        DateTimeOffset since,
        DateTimeOffset until,
        CancellationToken cancellationToken);
}
