using Quadra.Modules.InGame.Contracts;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Pure, side-effect-free MVP selection logic. Unit-tested in isolation.
/// </summary>
public static class MvpSelectionRules
{
    /// <summary>
    /// Selects the winning candidate from the given <paramref name="tallies"/>: highest vote count
    /// wins; ties are broken deterministically by lowest <see cref="MvpVoteTally.PlayerId"/>.
    /// Returns <c>(null, null)</c> when there are no votes.
    /// </summary>
    public static (Guid? PlayerId, int? VoteCount) SelectWinner(IReadOnlyList<MvpVoteTally> tallies)
    {
        ArgumentNullException.ThrowIfNull(tallies);

        if (tallies.Count == 0)
        {
            return (null, null);
        }

        var winner = tallies
            .OrderByDescending(t => t.Votes)
            .ThenBy(t => t.PlayerId)
            .First();

        return (winner.PlayerId, winner.Votes);
    }
}
