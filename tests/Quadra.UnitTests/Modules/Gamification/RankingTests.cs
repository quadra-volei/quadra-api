using FluentAssertions;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.UnitTests.Modules.Gamification;

/// <summary>Ordering, ties, minimum games and position filter of the season ranking (DECISIONS #63).</summary>
public sealed class RankingTests
{
    private static readonly Guid A = new("00000000-0000-0000-0000-00000000000a");
    private static readonly Guid B = new("00000000-0000-0000-0000-00000000000b");
    private static readonly Guid C = new("00000000-0000-0000-0000-00000000000c");
    private static readonly Guid D = new("00000000-0000-0000-0000-00000000000d");

    private static readonly IReadOnlyDictionary<Guid, PlayerSummary> Players = new Dictionary<Guid, PlayerSummary>
    {
        [A] = new(A, "A", "a", "LEV", "Beginner", null),
        [B] = new(B, "B", "b", "LIB", "Beginner", null),
        [C] = new(C, "C", "c", "LEV", "Beginner", null),
        [D] = new(D, "D", "d", null, "Beginner", null),
    };

    [Fact]
    public void Rank_orders_by_points_then_mvps_then_wins_then_fewer_games_and_shares_ties()
    {
        var board = GetRankingHandler.Rank(
            [
                new SeasonTotals(A, 50, 2, 1, 1),
                new SeasonTotals(B, 50, 2, 1, 1),
                new SeasonTotals(C, 50, 3, 3, 0),
                new SeasonTotals(D, 80, 4, 2, 1),
            ],
            Players,
            position: null,
            minMatches: 1);

        board.Select(e => (e.UserId, e.Rank)).Should().Equal((D, 1), (A, 2), (B, 2), (C, 4));
    }

    [Fact]
    public void Rank_drops_players_below_the_minimum_and_of_other_positions()
    {
        var totals = new[]
        {
            new SeasonTotals(A, 30, 3, 0, 0),
            new SeasonTotals(B, 90, 3, 2, 1),
            new SeasonTotals(C, 100, 2, 2, 1),
        };

        GetRankingHandler.Rank(totals, Players, "LEV", minMatches: 3).Select(e => e.UserId).Should().Equal(A);
        GetRankingHandler.Rank(totals, Players, null, minMatches: 3).Select(e => e.UserId).Should().Equal(B, A);
    }

    [Fact]
    public void Rank_by_mvps_or_wins_puts_that_count_first()
    {
        var totals = new[]
        {
            new SeasonTotals(A, 90, 3, 1, 0, Votes: 2),
            new SeasonTotals(B, 40, 3, 0, 1, Votes: 3),
            new SeasonTotals(C, 50, 3, 3, 0),
        };

        GetRankingHandler.Rank(totals, Players, null, 1, "mvps").Select(e => e.UserId).Should().Equal(B, A, C);
        GetRankingHandler.Rank(totals, Players, null, 1, "wins").Select(e => e.UserId).Should().Equal(C, A, B);
    }

    [Fact]
    public void Season_parses_a_month_in_brasilia_time()
    {
        GetRankingHandler.Season("2026-09").Should().Be((
            new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.FromHours(-3)),
            new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-3))));
    }

    [Fact]
    public void CurrentSeason_is_the_calendar_month_in_brasilia_time()
    {
        // 2026-11-01 01:00 UTC is still 31 October in Brasília (UTC-3).
        var (start, end) = GetRankingHandler.CurrentSeason(new DateTimeOffset(2026, 11, 1, 1, 0, 0, TimeSpan.Zero));

        start.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(-3)));
        end.Should().Be(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.FromHours(-3)));
    }
}
