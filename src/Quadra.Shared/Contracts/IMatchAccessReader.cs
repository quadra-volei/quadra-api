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

    /// <summary>The subset of <paramref name="matchIds"/> that <paramref name="callerId"/> may see.</summary>
    Task<IReadOnlySet<Guid>> ListViewableAsync(
        IReadOnlyCollection<Guid> matchIds,
        Guid callerId,
        CancellationToken cancellationToken);
}
