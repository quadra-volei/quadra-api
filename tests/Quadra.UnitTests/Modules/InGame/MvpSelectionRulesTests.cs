using FluentAssertions;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Contracts;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="MvpSelectionRules.SelectWinner"/> — the pure, deterministic MVP
/// selection function.
///
/// Covers (F1.5 AC-2 "automatic MVP selection from votes"):
///   - highest vote count wins;
///   - ties are broken deterministically by lowest PlayerId (Guid ordering);
///   - zero votes → (null, null) so the caller publishes no MvpAwarded.
/// </summary>
public sealed class MvpSelectionRulesTests
{
    private static readonly Guid Low = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Mid = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid High = new("33333333-3333-3333-3333-333333333333");

    /// <summary>
    /// Covers: F1.5 AC-2 — with zero votes the function returns (null, null); the close handler
    /// then sets MvpPlayerId=null and publishes no MvpAwarded.
    /// </summary>
    [Fact]
    public void SelectWinner_with_no_tallies_returns_null_playerId_and_null_count()
    {
        var (playerId, voteCount) = MvpSelectionRules.SelectWinner(Array.Empty<MvpVoteTally>());

        playerId.Should().BeNull();
        voteCount.Should().BeNull();
    }

    /// <summary>Covers: F1.5 AC-2 — a single candidate is selected with its vote count.</summary>
    [Fact]
    public void SelectWinner_with_single_tally_returns_that_candidate()
    {
        var tallies = new List<MvpVoteTally> { new(Mid, 4) };

        var (playerId, voteCount) = MvpSelectionRules.SelectWinner(tallies);

        playerId.Should().Be(Mid);
        voteCount.Should().Be(4);
    }

    /// <summary>Covers: F1.5 AC-2 — the candidate with the strictly highest count wins.</summary>
    [Fact]
    public void SelectWinner_picks_the_highest_vote_count()
    {
        var tallies = new List<MvpVoteTally>
        {
            new(Low, 2),
            new(Mid, 5),
            new(High, 3),
        };

        var (playerId, voteCount) = MvpSelectionRules.SelectWinner(tallies);

        playerId.Should().Be(Mid);
        voteCount.Should().Be(5);
    }

    /// <summary>
    /// Covers: F1.5 AC-2 — a tie on vote count is broken deterministically by the lowest PlayerId.
    /// </summary>
    [Fact]
    public void SelectWinner_breaks_ties_by_lowest_playerId()
    {
        var tallies = new List<MvpVoteTally>
        {
            new(High, 3),
            new(Low, 3),
            new(Mid, 3),
        };

        var (playerId, voteCount) = MvpSelectionRules.SelectWinner(tallies);

        playerId.Should().Be(Low, "ties must resolve to the lowest PlayerId for reproducibility");
        voteCount.Should().Be(3);
    }

    /// <summary>
    /// Covers: F1.5 AC-2 — when only the top two are tied, the lower-PlayerId of that pair wins,
    /// not a lower-PlayerId candidate that has fewer votes.
    /// </summary>
    [Fact]
    public void SelectWinner_tie_among_leaders_ignores_lower_ranked_lower_id()
    {
        var tallies = new List<MvpVoteTally>
        {
            new(Low, 1),   // lowest id but fewer votes — must NOT win
            new(High, 4),
            new(Mid, 4),
        };

        var (playerId, voteCount) = MvpSelectionRules.SelectWinner(tallies);

        playerId.Should().Be(Mid);
        voteCount.Should().Be(4);
    }

    /// <summary>Covers: F1.5 AC-2 — selection is independent of input ordering.</summary>
    [Fact]
    public void SelectWinner_is_independent_of_input_order()
    {
        var ascending = new List<MvpVoteTally> { new(Low, 3), new(Mid, 5), new(High, 1) };
        var descending = new List<MvpVoteTally> { new(High, 1), new(Mid, 5), new(Low, 3) };

        MvpSelectionRules.SelectWinner(ascending)
            .Should().Be(MvpSelectionRules.SelectWinner(descending));
    }
}
