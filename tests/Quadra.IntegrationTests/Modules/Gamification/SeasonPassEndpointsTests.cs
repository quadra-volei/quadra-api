using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Quadra.Modules.Gamification.Abstractions;
using Quadra.Modules.Gamification.Application;
using Quadra.Modules.Gamification.Contracts;

namespace Quadra.IntegrationTests.Modules.Gamification;

/// <summary><c>GET /api/v1/season-pass</c> (DECISIONS #98).</summary>
[Collection("Gamification")]
public sealed class SeasonPassEndpointsTests
{
    private readonly GamificationWebApplicationFactory _fx;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public SeasonPassEndpointsTests(GamificationWebApplicationFactory fx)
    {
        _fx = fx;
    }

    /// <summary>A finished recurring match where the player won three games: 10 + 24 = 34 points.</summary>
    private Task PlayAsync(Guid player, DateTimeOffset when) =>
        _fx.ApplyPointsAsync(new FinishedMatchPoints(
            Guid.NewGuid(), Guid.NewGuid(), when, null, [new FinishedMatchPlayerPoints(player, GamesWon: 3)], when));

    [Fact]
    public async Task The_pass_counts_the_season_points_with_the_weekly_cap()
    {
        await _fx.ResetAsync();
        var me = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var week = SeasonPassRules.WeekStart(now).ToUniversalTime(); // Npgsql writes only UTC offsets
        await PlayAsync(me, week);
        await PlayAsync(me, week.AddSeconds(1));         // same week: 68 counts as 60
        await PlayAsync(me, now.AddDays(-10));           // another week: 34
        await PlayAsync(me, now.AddDays(-70));           // before the season

        var pass = await _fx.CreateAuthenticatedClient(me).GetFromJsonAsync<SeasonPassResponse>("/api/v1/season-pass", Ct);

        pass!.Should().BeEquivalentTo(new
        {
            Season = "T1",
            Status = "Active",
            Points = 94,
            Level = 4,
            Levels = 12,
            PointsPerLevel = 20,
            WeeklyCap = 60,
            WeekPoints = 60,
            Premium = true,
        });
        pass.WeekEndsAt.Should().BeAfter(now);
        pass.Rewards.Where(r => r.Unlocked).Select(r => r.Code)
            .Should().Equal("PIECE_1", "PIECE_2", "PIECE_3", "SEMPRE_PRESENTE", "PIECE_4");
        pass.Rewards.Should().HaveCount(16);
    }

    [Fact]
    public async Task A_player_who_has_not_played_starts_at_zero()
    {
        await _fx.ResetAsync();

        var pass = await _fx.CreateAuthenticatedClient(Guid.NewGuid()).GetFromJsonAsync<SeasonPassResponse>("/api/v1/season-pass", Ct);

        pass!.Points.Should().Be(0);
        pass.Level.Should().Be(0);
        pass.Rewards.Should().OnlyContain(r => !r.Unlocked);
    }

    [Fact]
    public async Task Anonymous_caller_is_refused()
    {
        (await _fx.CreateAnonymousClient().GetAsync("/api/v1/season-pass", Ct))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
