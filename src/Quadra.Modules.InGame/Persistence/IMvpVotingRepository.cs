using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// Persistence contract for the <see cref="MvpVoting"/> aggregate (voting session + its votes).
/// </summary>
public interface IMvpVotingRepository
{
    /// <summary>
    /// Inserts a new <paramref name="voting"/> session in a single save.
    /// </summary>
    Task AddAsync(MvpVoting voting, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the voting session for <paramref name="matchId"/> with its votes attached,
    /// or <c>null</c> if none exists. Entities are change-tracked so callers can mutate them and
    /// persist via <see cref="UpdateAsync"/>.
    /// </summary>
    Task<MvpVoting?> FindByMatchAsync(Guid matchId, CancellationToken cancellationToken);

    /// <summary>
    /// Upserts the caller's single ballot inside a single transaction:
    /// inserts a new vote and increments <c>total_votes</c> when none exists, or updates the
    /// existing vote's <c>voted_player_id</c> otherwise. Returns the resulting vote.
    /// </summary>
    Task<MvpVote> CastVoteAsync(
        Guid votingId,
        Guid matchId,
        Guid voterPlayerId,
        Guid votedPlayerId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    /// <summary>
    /// Persists pending changes to <paramref name="voting"/> inside a single database transaction.
    /// </summary>
    Task UpdateAsync(MvpVoting voting, CancellationToken cancellationToken);
}
