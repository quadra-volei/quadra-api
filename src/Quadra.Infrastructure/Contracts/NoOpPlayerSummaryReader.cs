using Quadra.Shared.Contracts;

namespace Quadra.Infrastructure.Contracts;

/// <summary>
/// Fallback <see cref="IPlayerSummaryReader"/> for hosts that do not load the Profile module:
/// every player is the placeholder "Player", Beginner, with no handle, position or photo.
/// </summary>
public sealed class NoOpPlayerSummaryReader : IPlayerSummaryReader
{
    public const string PlaceholderName = "Player";

    public Task<IReadOnlyDictionary<Guid, PlayerSummary>> GetSummariesAsync(
        IReadOnlyCollection<Guid> playerIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(playerIds);

        IReadOnlyDictionary<Guid, PlayerSummary> result = playerIds
            .Distinct()
            .ToDictionary(id => id, Placeholder);
        return Task.FromResult(result);
    }

    public static PlayerSummary Placeholder(Guid userId) =>
        new(userId, PlaceholderName, Handle: null, Position: null, Level: "Beginner", PhotoUrl: null);
}
