namespace Quadra.Shared.Realtime;

/// <summary>
/// Abstraction that broadcasts real-time messages to all clients connected to a match room.
/// The interface lives in <c>Quadra.Shared</c>; the implementation lives in <c>Quadra.Modules.Realtime</c>.
/// The Matches module depends on this interface only — never on the Realtime module directly.
/// </summary>
public interface IMatchRoomNotifier
{
    /// <summary>
    /// Sends a <see cref="PresenceUpdatedMessage"/> to all clients in the SignalR group
    /// <c>match:{matchId}</c>.
    /// </summary>
    Task NotifyPresenceUpdatedAsync(PresenceUpdatedMessage message, CancellationToken cancellationToken);

    /// <summary>
    /// Sends a <see cref="ScoreboardUpdatedMessage"/> to all clients in the SignalR group
    /// <c>match:{matchId}</c>.
    /// </summary>
    Task NotifyScoreboardUpdatedAsync(ScoreboardUpdatedMessage message, CancellationToken cancellationToken);
}
