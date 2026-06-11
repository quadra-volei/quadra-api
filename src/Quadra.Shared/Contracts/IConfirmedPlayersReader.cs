namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module query interface for confirmed players in a match.
/// Declared in <c>Quadra.Shared</c>; implemented in <c>Quadra.Modules.Matches</c>.
/// </summary>
public interface IConfirmedPlayersReader
{
    /// <summary>
    /// Returns the IDs of all players with a confirmed presence for the given <paramref name="matchId"/>.
    /// Returns an empty list if the match has no confirmed players.
    /// </summary>
    Task<IReadOnlyList<Guid>> GetConfirmedPlayerIdsAsync(Guid matchId, CancellationToken cancellationToken);
}
