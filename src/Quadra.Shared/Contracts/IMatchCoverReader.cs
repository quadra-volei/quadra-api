namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module query for match covers. Declared in <c>Quadra.Shared</c>; implemented
/// in <c>Quadra.Modules.Matches</c>. Consumed by Geo (nearby matches) and Profile (the feed).
/// </summary>
public interface IMatchCoverReader
{
    /// <summary>
    /// Short-lived read URLs of the covers of <paramref name="matchIds"/>, by match id. A match
    /// with no cover (or no photo storage configured) is simply absent.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetCoverUrlsAsync(
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken);
}
