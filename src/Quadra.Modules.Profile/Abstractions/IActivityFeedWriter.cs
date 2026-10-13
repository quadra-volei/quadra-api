namespace Quadra.Modules.Profile.Abstractions;

/// <summary>Something a player did in a match that the "Rede" feed tells others about.</summary>
public enum MatchActivity
{
    /// <summary>The player created the match.</summary>
    Created,

    /// <summary>The player confirmed presence in the match.</summary>
    Confirmed,
}

/// <summary>
/// Profile-owned write interface that puts an automatic activity card in the "Rede" feed.
/// Invoked by the host's event handlers for <c>MatchCreated</c> and <c>PresenceConfirmed</c>.
/// </summary>
public interface IActivityFeedWriter
{
    /// <summary>
    /// Adds the card "<paramref name="playerId"/> did <paramref name="activity"/> in the match".
    /// A no-op when that player already has that card for that match (covers redelivery, and a
    /// player who confirms, declines and confirms again).
    /// </summary>
    Task AddMatchActivityAsync(
        Guid playerId,
        MatchActivity activity,
        Guid matchId,
        string matchName,
        DateTimeOffset matchStartsAt,
        CancellationToken cancellationToken);
}
