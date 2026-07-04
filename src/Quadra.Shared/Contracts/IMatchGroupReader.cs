namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module query interface exposing a match's grouping identity (grouping key, type,
/// name and scheduled time). Declared in <c>Quadra.Shared.Contracts</c>; implemented in
/// <c>Quadra.Modules.Matches</c>. Consumed by the Gamification module (group-ranking read path) and
/// the Background Worker (finished-match points composition).
/// </summary>
public interface IMatchGroupReader
{
    /// <summary>
    /// Returns the group descriptor for <paramref name="matchId"/>, or <c>null</c> when no match exists.
    /// </summary>
    Task<MatchGroupDescriptor?> GetMatchGroupAsync(Guid matchId, CancellationToken cancellationToken);
}
