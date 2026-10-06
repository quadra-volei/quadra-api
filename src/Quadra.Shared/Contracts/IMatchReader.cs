namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module query interface for match data.
/// Declared in <c>Quadra.Shared</c>; implemented in <c>Quadra.Modules.Matches</c>.
/// </summary>
public interface IMatchReader
{
    /// <summary>
    /// Returns a minimal projection of the match identified by <paramref name="matchId"/>,
    /// or <c>null</c> if no match with that ID exists.
    /// </summary>
    Task<MatchSummary?> FindMatchSummaryAsync(Guid matchId, CancellationToken cancellationToken);
}
