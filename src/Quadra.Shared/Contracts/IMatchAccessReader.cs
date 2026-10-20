namespace Quadra.Shared.Contracts;

/// <summary>
/// The single "who may see a match" rule (CanViewMatch): the match is open, or the caller is its
/// organizer or a participant (on the presence list in any status, on the waiting list, or the one
/// who added a guest). Every read of a match or of its game data goes through it; a denied caller
/// gets 404, as if the match did not exist. Declared in <c>Quadra.Shared</c>; implemented in
/// <c>Quadra.Modules.Matches</c>.
/// </summary>
public interface IMatchAccessReader
{
    /// <summary>Whether <paramref name="callerId"/> may see the match. False when it does not exist.</summary>
    Task<bool> CanViewAsync(Guid matchId, Guid callerId, CancellationToken cancellationToken);

    /// <summary>
    /// Whether <paramref name="inviteCode"/> is the invite code of this private match: the holder of
    /// an invite link may see the match before joining (DECISIONS #73). Never true for a match
    /// without a code.
    /// </summary>
    Task<bool> IsValidInviteAsync(Guid matchId, string inviteCode, CancellationToken cancellationToken);

    /// <summary>The subset of <paramref name="matchIds"/> that <paramref name="callerId"/> may see.</summary>
    Task<IReadOnlySet<Guid>> ListViewableAsync(
        IReadOnlyCollection<Guid> matchIds,
        Guid callerId,
        CancellationToken cancellationToken);

    /// <summary>
    /// The subset of <paramref name="matchIds"/> that are open (public) right now, whoever asks. Used
    /// where a match is shown to people who are not in it (the activity feed).
    /// </summary>
    Task<IReadOnlySet<Guid>> ListPublicAsync(IReadOnlyCollection<Guid> matchIds, CancellationToken cancellationToken);
}
