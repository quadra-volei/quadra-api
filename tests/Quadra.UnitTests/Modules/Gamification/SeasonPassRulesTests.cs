using FluentAssertions;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Persistence;

namespace Quadra.UnitTests.Modules.Gamification;

/// <summary>Weekly cap, levels, rewards and season choice of the season pass (DECISIONS #98).</summary>
public sealed class SeasonPassRulesTests
{
    private static readonly TimeSpan Brt = TimeSpan.FromHours(-3);

    private static readonly Season T1 = SeasonPassRules.Seasons(new SeasonPassOptions
    {
        Seasons = { ["T1"] = new SeasonDates { StartsOn = new DateOnly(2026, 11, 1), EndsOn = new DateOnly(2027, 2, 1) } },
    }).Single();

    private static DateTimeOffset At(int month, int day, int hour, int year = 2026) => new(year, month, day, hour, 0, 0, Brt);

    [Fact]
    public void Progress_caps_each_week_and_counts_only_the_season()
    {
        var progress = SeasonPassRules.Progress(
            T1,
            [
                new DatedPoints(At(10, 31, 20), 50),   // the day before the season
                new DatedPoints(At(11, 3, 20), 40),
                new DatedPoints(At(11, 7, 10), 40),    // same week: 80 counts as 60
                new DatedPoints(At(11, 10, 20), 26),
            ],
            now: At(11, 11, 12));

        progress.Should().Be(new SeasonProgress(86, 4, 26, At(11, 16, 0)));
    }

    [Fact]
    public void A_week_runs_from_Monday_to_Sunday_in_Brasilia()
    {
        SeasonPassRules.WeekStart(At(11, 8, 23)).Should().Be(At(11, 2, 0));
        SeasonPassRules.WeekStart(new DateTimeOffset(2026, 11, 9, 2, 30, 0, TimeSpan.Zero)).Should().Be(At(11, 2, 0), "23:30 of Sunday in Brasília");
        SeasonPassRules.WeekStart(At(11, 9, 0)).Should().Be(At(11, 9, 0));
    }

    [Fact]
    public void The_level_stops_at_the_last_one_and_an_ended_season_has_no_current_week()
    {
        var weeks = Enumerable.Range(0, 10).Select(w => new DatedPoints(At(11, 3, 20).AddDays(7 * w), 60));

        var progress = SeasonPassRules.Progress(T1, weeks, now: At(2, 1, 0, 2027));

        progress.Should().Be(new SeasonProgress(600, SeasonPassRules.Levels, 0, null));
    }

    [Fact]
    public void The_last_week_ends_with_the_season()
    {
        SeasonPassRules.Progress(T1, [], now: At(1, 30, 12, 2027)).WeekEndsAt.Should().Be(At(2, 1, 0, 2027));
    }

    [Fact]
    public void Rewards_are_a_piece_per_level_titles_on_the_free_track_and_the_finish_at_the_end()
    {
        var rewards = SeasonPassRules.Rewards(T1.Definition);

        rewards.Where(r => r.Kind == "Piece").Should().HaveCount(SeasonPassRules.Levels).And.OnlyContain(r => r.Track == SeasonPassRules.Premium);
        rewards.Where(r => r.Kind == "Title").Select(r => (r.Level, r.Code, r.Track))
            .Should().Equal((3, "SEMPRE_PRESENTE", "Free"), (6, "CRIA_DA_QUADRA", "Free"), (9, "LENDA_T1", "Free"));
        rewards.Last().Should().Be(new SeasonReward(SeasonPassRules.Levels, SeasonPassRules.Premium, "Finish", "Pioneira"));
    }

    [Fact]
    public void A_free_reward_needs_the_level_and_a_paid_one_also_the_pass()
    {
        var level6 = new SeasonProgress(120, 6, 0, null);
        var title = new SeasonReward(6, SeasonPassRules.Free, "Title", "CRIA_DA_QUADRA");
        var piece = new SeasonReward(6, SeasonPassRules.Premium, "Piece", "PIECE_6");

        SeasonPassRules.IsUnlocked(title, level6, premium: false).Should().BeTrue();
        SeasonPassRules.IsUnlocked(piece, level6, premium: false).Should().BeFalse();
        SeasonPassRules.IsUnlocked(piece, level6, premium: true).Should().BeTrue();
        SeasonPassRules.IsUnlocked(piece with { Level = 7 }, level6, premium: true).Should().BeFalse();
    }

    [Fact]
    public void The_season_shown_is_the_running_one_else_the_next_else_the_last()
    {
        var seasons = new[] { T1 };

        SeasonPassRules.Current(seasons, At(10, 27, 12)).Should().Be(T1);
        SeasonPassRules.Current(seasons, At(12, 25, 12)).Should().Be(T1);
        SeasonPassRules.Current(seasons, At(3, 1, 12, 2027)).Should().Be(T1);
        SeasonPassRules.Current([], At(12, 25, 12)).Should().BeNull();
    }

    [Theory]
    [InlineData("T1", "2026-11-01", "2027-02-01", true)]
    [InlineData("T9", "2026-11-01", "2027-02-01", false)]
    [InlineData("T1", "2027-02-01", "2026-11-01", false)]
    public void Options_accept_only_known_seasons_that_end_after_they_start(string code, string start, string end, bool valid)
    {
        var options = new SeasonPassOptions
        {
            Seasons = { [code] = new SeasonDates { StartsOn = DateOnly.Parse(start), EndsOn = DateOnly.Parse(end) } },
        };

        options.IsValid().Should().Be(valid);
    }
}
