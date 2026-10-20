namespace Quadra.Shared.Contracts;

/// <summary>A match a player may invite others to: its name, scheduled time and organizer.</summary>
public sealed record MatchInvite(Guid MatchId, string Name, DateTimeOffset DateTime, Guid OrganizerId);

/// <summary>
/// Read-only cross-module query for the matches a player may publicly invite others to.
/// Declared in <c>Quadra.Shared</c>; implemented in <c>Quadra.Modules.Matches</c>. Consumed by
/// the Profile module when a post is tied to a match that has not been played yet.
/// </summary>
public interface IMatchInviteReader
{
    /// <summary>
    /// Returns the match when <paramref name="playerId"/> may invite to it: it is open to
    /// everyone (not private), has not started or been cancelled, and the player organizes it
    /// or has a confirmed presence. Null otherwise, including when the match does not exist.
    /// </summary>
    Task<MatchInvite?> FindInvitableAsync(Guid matchId, Guid playerId, CancellationToken cancellationToken);
}
