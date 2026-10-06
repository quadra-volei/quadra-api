using Quadra.Shared.Contracts;

namespace Quadra.Infrastructure.Contracts;

/// <summary>
/// No-op implementation of <see cref="IPlayerLevelReader"/> that returns <c>"Beginner"</c>
/// for every requested player ID. Used until F2.1 (Profile module) delivers the real implementation.
/// Registered with <c>TryAddScoped</c> so Profile can override it.
/// </summary>
public sealed class NoOpPlayerLevelReader : IPlayerLevelReader
{
    public Task<IReadOnlyDictionary<Guid, string>> GetPlayerLevelsAsync(
        IReadOnlyList<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, string> result =
            playerIds.ToDictionary(id => id, _ => "Beginner");

        return Task.FromResult(result);
    }
}
