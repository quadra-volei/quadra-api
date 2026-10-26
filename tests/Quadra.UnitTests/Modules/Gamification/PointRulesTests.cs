using FluentAssertions;
using Quadra.Modules.Gamification.Abstractions;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Entities;

namespace Quadra.UnitTests.Modules.Gamification;

/// <summary>
/// Unit tests for <see cref="PointRules"/> (DECISIONS #95): attendance +10, +8 per game won (at
/// most 3), +4 per MVP vote, MVP +20 only with 3+ ballots. The DB recompute is verified against
/// real Postgres in <c>ApplyFinishedMatchPointsTests</c>.
/// </summary>
public sealed class PointRulesTests
{
    private static readonly Guid Star = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Other = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MatchId = new("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset MatchDateTime = new(2026, 7, 1, 20, 0, 0, TimeSpan.Zero);

    private static FinishedMatchPoints Build(Guid? mvp, int totalVotes, params FinishedMatchPlayerPoints[] players) =>
        new(MatchId, MatchId, MatchDateTime, mvp, players, MatchDateTime.AddHours(2), totalVotes);

    [Fact]
    public void BuildAwards_mvp_with_two_games_and_five_votes_scores_66()
    {
        var awards = PointRules.BuildAwards(Build(Star, 10, new FinishedMatchPlayerPoints(Star, GamesWon: 2, MvpVotes: 5)));

        awards.Should().BeEquivalentTo(new[]
        {
            (Star, PointReason.Attendance, 10, 1),
            (Star, PointReason.GameWin, 16, 2),
            (Star, PointReason.MvpVote, 20, 5),
            (Star, PointReason.Mvp, 20, 1),
        });
        awards.Sum(a => a.Points).Should().Be(66);
    }

    [Fact]
    public void BuildAwards_attendance_only_when_no_game_won_and_no_vote()
    {
        PointRules.BuildAwards(Build(null, 0, new FinishedMatchPlayerPoints(Other)))
            .Should().ContainSingle().Which.Should().Be((Other, PointReason.Attendance, 10, 1));
    }

    [Fact]
    public void BuildAwards_counts_at_most_three_games()
    {
        PointRules.BuildAwards(Build(null, 0, new FinishedMatchPlayerPoints(Other, GamesWon: 5)))
            .Should().Contain((Other, PointReason.GameWin, 24, 3));
    }

    [Fact]
    public void BuildAwards_mvp_bonus_needs_three_ballots_but_votes_always_count()
    {
        var awards = PointRules.BuildAwards(Build(Star, 2, new FinishedMatchPlayerPoints(Star, MvpVotes: 2)));

        awards.Should().NotContain(a => a.Reason == PointReason.Mvp);
        awards.Should().Contain((Star, PointReason.MvpVote, 8, 2));
    }

    [Fact]
    public void BuildAwards_mvp_goes_to_exactly_one_player()
    {
        var awards = PointRules.BuildAwards(Build(
            Star,
            4,
            new FinishedMatchPlayerPoints(Star, MvpVotes: 3),
            new FinishedMatchPlayerPoints(Other, MvpVotes: 1)));

        awards.Where(a => a.Reason == PointReason.Mvp).Select(a => a.UserId).Should().Equal(Star);
        awards.Where(a => a.UserId == Other).Sum(a => a.Points).Should().Be(14);
    }
}
