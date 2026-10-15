using FluentAssertions;
using Quadra.Modules.Matches.Entities;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>The next game of a recurring group: its date and what it copies (DECISIONS #64).</summary>
public sealed class MatchNextGameTests
{
    private static readonly TimeSpan Brasilia = TimeSpan.FromHours(-3);

    // Tuesday 13 and Thursday 15 October 2026, 20:00 in Brasília.
    private static readonly DateTimeOffset Tuesday = new(2026, 10, 13, 20, 0, 0, Brasilia);
    private static readonly DateTimeOffset Thursday = new(2026, 10, 15, 20, 0, 0, Brasilia);

    private static DateTimeOffset Next(DateTimeOffset current, MatchFrequency? frequency, int[]? days, DateTimeOffset? now = null) =>
        Match.NextGameTime(current, frequency, days, now ?? current);

    [Fact]
    public void Weekly_on_one_day_is_the_same_day_next_week_at_the_same_time()
    {
        Next(Tuesday, MatchFrequency.Weekly, [2]).Should().Be(Tuesday.AddDays(7));
        Next(Tuesday, null, null).Should().Be(Tuesday.AddDays(7));
    }

    [Fact]
    public void Weekly_on_two_days_alternates_between_them()
    {
        Next(Tuesday, MatchFrequency.Weekly, [2, 4]).Should().Be(Thursday);
        Next(Thursday, MatchFrequency.Weekly, [2, 4]).Should().Be(Tuesday.AddDays(7));
    }

    [Fact]
    public void Biweekly_and_monthly_skip_weeks_only_when_the_week_ends()
    {
        Next(Tuesday, MatchFrequency.Biweekly, [2, 4]).Should().Be(Thursday);
        Next(Thursday, MatchFrequency.Biweekly, [2, 4]).Should().Be(Tuesday.AddDays(14));
        Next(Tuesday, MatchFrequency.Monthly, [2]).Should().Be(Tuesday.AddDays(28));
    }

    [Fact]
    public void A_late_summary_rolls_to_the_first_game_still_ahead()
    {
        Next(Tuesday, MatchFrequency.Weekly, [2], now: Tuesday.AddDays(9)).Should().Be(Tuesday.AddDays(14));
    }

    [Fact]
    public void CreateNext_copies_the_game_into_the_group_with_the_same_window_distance()
    {
        var created = Tuesday.AddDays(-3);
        var first = Match.Create(
            organizerId: Guid.NewGuid(),
            name: "Terça do vôlei",
            description: "Quadra 2",
            address: "Rua A",
            latitude: -23.5,
            longitude: -46.6,
            dateTime: Tuesday,
            maxPlayers: 12,
            regularSlots: 10,
            price: 20,
            type: MatchType.Recurring,
            frequency: MatchFrequency.Weekly,
            dayOfWeekIso: 2,
            windowOpensAt: Tuesday.AddDays(-2),
            windowClosesAt: Tuesday.AddHours(-1),
            now: created,
            settings: new MatchSettings(Visibility: MatchVisibility.Private, InviteMode: MatchInviteMode.Code));

        var now = Tuesday.AddHours(3);
        var second = first.CreateNext(Tuesday.AddDays(7), now);
        var third = second.CreateNext(Tuesday.AddDays(14), now);

        second.Id.Should().NotBe(first.Id);
        second.GroupId.Should().Be(first.Id);
        third.GroupId.Should().Be(first.Id);
        second.Status.Should().Be(MatchStatus.Draft);
        second.WindowOpensAt.Should().Be(Tuesday.AddDays(5));
        second.WindowClosesAt.Should().Be(Tuesday.AddDays(7).AddHours(-1));
        second.InviteCode.Should().Be(first.InviteCode).And.NotBeNull();
        second.Should().BeEquivalentTo(first, o => o
            .Including(m => m.Name).Including(m => m.Address).Including(m => m.MaxPlayers)
            .Including(m => m.RegularSlots).Including(m => m.Price).Including(m => m.Visibility)
            .Including(m => m.OrganizerId).Including(m => m.Frequency));
        first.GroupId.Should().BeNull();
    }

    [Fact]
    public void CreateNext_never_opens_the_window_before_now()
    {
        var first = Match.Create(
            Guid.NewGuid(), "Racha", null, "Rua A", -23.5, -46.6, Tuesday, 12, 12, null,
            MatchType.Recurring, MatchFrequency.Weekly, 2,
            windowOpensAt: Tuesday.AddDays(-20), windowClosesAt: Tuesday, now: Tuesday.AddDays(-20));
        var now = Tuesday.AddHours(3);

        first.CreateNext(Tuesday.AddDays(7), now).WindowOpensAt.Should().Be(now);
    }
}
