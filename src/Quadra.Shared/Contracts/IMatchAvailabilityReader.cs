namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module query interface for per-match slot availability.
/// Declared in <c>Quadra.Shared</c>; implemented in <c>Quadra.Modules.Matches</c>.
/// </summary>
public interface IMatchAvailabilityReader
{
    /// <summary>
    /// Returns availability for the requested match IDs; IDs with no match are omitted from the dictionary.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, MatchAvailability>> GetAvailabilityAsync(
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken);
}
