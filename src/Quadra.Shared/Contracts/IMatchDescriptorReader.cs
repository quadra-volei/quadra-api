namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module query interface exposing a match's descriptive fields.
/// Declared in <c>Quadra.Shared.Contracts</c>; implemented in <c>Quadra.Modules.Matches</c>.
/// Consumed by the Background Worker (F2.1) to snapshot a finished match's name and date.
/// </summary>
public interface IMatchDescriptorReader
{
    /// <summary>
    /// Returns the descriptor for <paramref name="matchId"/>, or <c>null</c> when the match does not exist.
    /// </summary>
    Task<MatchDescriptor?> GetMatchDescriptorAsync(Guid matchId, CancellationToken cancellationToken);
}
