namespace Quadra.Shared.Contracts;

/// <summary>
/// Which preference switch of the app controls a notification's push (the in-app list always
/// keeps it): <c>Reminders</c> (matches tomorrow, a spot from the waiting list), <c>Ranking</c>
/// (MVP voting, summary and points), <c>Invites</c>.
/// </summary>
public enum NotificationCategory
{
    Reminders,
    Ranking,
    Invites,
}

/// <param name="Type">Stable code the app may read (e.g. <c>MatchTomorrow</c>).</param>
/// <param name="DedupKey">Same key for the same user = sent once (e.g. <c>tomorrow:{matchId}</c>).</param>
public sealed record NotificationRequest(
    string Type,
    NotificationCategory Category,
    string Title,
    string Body,
    Guid? MatchId,
    string DedupKey);

/// <summary>
/// Implemented by the Notifications module: stores the notification for each user (in-app list,
/// once per <see cref="NotificationRequest.DedupKey"/>) and pushes it to their phones when their
/// preference allows. Never throws for a failed push.
/// </summary>
public interface INotificationSender
{
    Task SendAsync(IReadOnlyCollection<Guid> userIds, NotificationRequest notification, CancellationToken cancellationToken);
}

/// <summary>A match about to happen and who is on its list (Matches implements it).</summary>
public sealed record UpcomingMatch(Guid MatchId, string Name, DateTimeOffset StartsAt, IReadOnlyList<UpcomingPlayer> Players);

/// <param name="Confirmed">False while the presence is still pending (the player must confirm).</param>
public sealed record UpcomingPlayer(Guid PlayerId, bool Confirmed);

public interface IUpcomingMatchReader
{
    /// <summary>Matches neither cancelled nor ended starting in (<paramref name="from"/>, <paramref name="to"/>], with their pending and confirmed players.</summary>
    Task<IReadOnlyList<UpcomingMatch>> ListStartingBetweenAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
