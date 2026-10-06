namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module query interface for player level data.
/// Declared in <c>Quadra.Shared</c>; implemented in <c>Quadra.Modules.Profile</c> (F2.1).
/// Until F2.1 is delivered, <c>Quadra.Infrastructure</c> registers a no-op implementation
/// that returns <c>"Beginner"</c> for every player.
/// </summary>
public interface IPlayerLevelReader
{
    /// <summary>
    /// Returns a dictionary mapping each player ID in <paramref name="playerIds"/> to their
    /// current level string (one of <c>Beginner</c>, <c>Intermediate</c>, <c>Advanced</c>, <c>Elite</c>).
    /// </summary>
    Task<IReadOnlyDictionary<Guid, string>> GetPlayerLevelsAsync(
        IReadOnlyList<Guid> playerIds,
        CancellationToken cancellationToken);
}
