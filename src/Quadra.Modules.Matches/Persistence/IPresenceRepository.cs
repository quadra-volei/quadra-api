using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Persistence contract for the <see cref="MatchPresence"/> entity.
/// </summary>
public interface IPresenceRepository
{
    Task AddAsync(MatchPresence presence, CancellationToken cancellationToken);

    Task<MatchPresence?> FindByMatchAndPlayerAsync(
        Guid matchId,
        Guid playerId,
        CancellationToken cancellationToken);

    Task UpdateAsync(MatchPresence presence, CancellationToken cancellationToken);

    Task DeleteAsync(MatchPresence presence, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all presence rows for the given match, ordered by <c>created_at ASC</c>.
    /// </summary>
    Task<IReadOnlyList<MatchPresence>> ListByMatchAsync(Guid matchId, CancellationToken cancellationToken);

    /// <summary>
    /// Counts confirmed presences for a given match and player type.
    /// </summary>
    Task<int> CountConfirmedAsync(Guid matchId, PlayerType playerType, CancellationToken cancellationToken);

    /// <summary>Confirmed presences of the match, whatever the player type.</summary>
    Task<int> CountConfirmedAsync(Guid matchId, CancellationToken cancellationToken);

    /// <summary>Every presence row of the given matches.</summary>
    Task<IReadOnlyList<MatchPresence>> ListByMatchesAsync(
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken);
}
