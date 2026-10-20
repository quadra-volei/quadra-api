namespace Quadra.Shared.Contracts;

/// <summary>
/// The public identity of a player, as other modules need to show it (match rosters, rankings).
/// </summary>
/// <param name="UserId">The player's user id.</param>
/// <param name="DisplayName">"First Last", or a placeholder while the player has no name yet.</param>
/// <param name="Handle">Unique handle without the leading <c>@</c>; null until onboarding.</param>
/// <param name="Position">Court position code (LEV, PON, OPO, CEN, LIB, COR); null when not set.</param>
/// <param name="Level">Beginner | Intermediate | Advanced | Elite.</param>
/// <param name="PhotoUrl">Short-lived photo URL; null when the player has no photo.</param>
public sealed record PlayerSummary(
    Guid UserId,
    string DisplayName,
    string? Handle,
    string? Position,
    string Level,
    string? PhotoUrl);

/// <summary>
/// Cross-module read interface over player identity, implemented by the Profile module.
/// Modules that render players (Matches, Gamification) ask through it instead of touching
/// Profile tables.
/// </summary>
public interface IPlayerSummaryReader
{
    /// <summary>
    /// Returns a summary for every id in <paramref name="playerIds"/>. A player without a
    /// profile still gets an entry (placeholder name, Beginner), so callers never need to
    /// handle a missing key.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, PlayerSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken);
}
