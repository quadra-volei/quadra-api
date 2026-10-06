using Quadra.Modules.Profile.Entities;

namespace Quadra.Modules.Profile.Persistence;

/// <summary>
/// Result of a player-card lookup. Distinguishes "no profile at all" from "profile exists but no
/// card yet generated" so the read path can map the two to <c>404</c> vs <c>409</c>.
/// </summary>
/// <param name="Card">The card row, or <c>null</c> when none has been generated.</param>
/// <param name="ProfileExists">Whether a profile exists for the user.</param>
/// <param name="MatchesPlayed">
/// The player's current recorded-match count (from the card when present, otherwise from stats).
/// Used to populate the "card not generated" response body.
/// </param>
public sealed record PlayerCardLookup(PlayerCard? Card, bool ProfileExists, int MatchesPlayed);

/// <summary>Persistence contract for <see cref="PlayerCard"/> rows.</summary>
public interface IPlayerCardRepository
{
    /// <summary>
    /// Loads the card for <paramref name="userId"/> together with the profile-exists / matches-played
    /// signals needed to distinguish a missing profile from an ungenerated card.
    /// </summary>
    Task<PlayerCardLookup> FindByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Inserts a new card or updates the existing one identified by <c>user_id</c>.</summary>
    Task UpsertAsync(PlayerCard card, CancellationToken cancellationToken);
}
