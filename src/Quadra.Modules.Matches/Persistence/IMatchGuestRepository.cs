using Quadra.Modules.Matches.Entities;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>Persistence operations on <c>match_guests</c>. Scoped lifetime.</summary>
public interface IMatchGuestRepository
{
    Task AddAsync(MatchGuest guest, CancellationToken cancellationToken);

    Task<MatchGuest?> FindAsync(Guid matchId, Guid guestId, CancellationToken cancellationToken);

    Task RemoveAsync(MatchGuest guest, CancellationToken cancellationToken);

    /// <summary>The guests of a match, oldest first.</summary>
    Task<IReadOnlyList<MatchGuest>> ListByMatchAsync(Guid matchId, CancellationToken cancellationToken);

    Task<int> CountByMatchAsync(Guid matchId, CancellationToken cancellationToken);

    /// <summary>Guest count per match, for the matches that have any.</summary>
    Task<IReadOnlyDictionary<Guid, int>> CountByMatchesAsync(
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken);
}
