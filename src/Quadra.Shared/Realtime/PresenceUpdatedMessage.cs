namespace Quadra.Shared.Realtime;

/// <summary>
/// SignalR payload sent to the <c>match:{matchId}</c> group whenever a presence row changes status
/// (confirm, decline, or waiter promoted).
/// </summary>
public sealed record PresenceUpdatedMessage(
    Guid MatchId,
    Guid PlayerId,
    string PlayerType,
    string Status,
    int ConfirmedCount,
    DateTimeOffset OccurredAt);
