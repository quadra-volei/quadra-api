namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module query interface for a match's finished-game result.
/// Declared in <c>Quadra.Shared</c>; implemented in <c>Quadra.Modules.InGame</c>.
/// This is the inverse of <see cref="IMatchReader"/> (implemented by Matches for InGame):
/// here InGame exposes its scoreboard, teams and MVP outcome to the Matches module.
/// </summary>
public interface IMatchResultReader
{
    /// <summary>
    /// Returns the current result projection for the match identified by <paramref name="matchId"/>,
    /// or <c>null</c> when no scoreboard has been created for the match.
    /// </summary>
    Task<MatchResult?> GetMatchResultAsync(Guid matchId, CancellationToken cancellationToken);
}
