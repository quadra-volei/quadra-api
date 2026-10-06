using Quadra.Modules.Gamification.Entities;

namespace Quadra.Modules.Gamification.Persistence;

/// <summary>The latest match a player attended within a group.</summary>
public sealed record AttendedMatch(Guid MatchId, DateTimeOffset MatchDateTime);

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
}
