using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Infrastructure.Messaging;
using Quadra.IntegrationTests.SecurityAudit;
using Quadra.Modules.Matches.Persistence;
using Quadra.Modules.Profile.Activities;
using Quadra.Modules.Profile.Persistence;
using Quadra.Shared.Events.InGame;

namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>
/// "Atividade dos amigos" end to end: real logins, a real game played through the API, the
/// in-process events recording activities, and <c>GET /api/v1/activities</c> as a follower sees it.
/// </summary>
public sealed class ActivitiesEndpointsTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public ActivitiesEndpointsTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Follower_sees_the_game_start_grouped_and_a_stranger_sees_nothing()
    {
        var game = await StartGameAsync(open: true);
        var follower = await _fx.LoginAsync();
        var stranger = await _fx.LoginAsync();
        await FollowAsync(follower, game.Players[0]);
        await FollowAsync(follower, game.Players[1]);

        var feed = await FeedAsync(follower);
        var started = feed.Should().ContainSingle().Subject;
        started.GetProperty("type").GetString().Should().Be("MatchStarted");
        started.GetProperty("match").GetProperty("id").GetGuid().Should().Be(game.MatchId);
        started.GetProperty("match").GetProperty("name").GetString().Should().Be("Racha da Galera");
        started.GetProperty("othersCount").GetInt32().Should().Be(1);
        started.GetProperty("actor").GetProperty("level").GetInt32().Should().Be(1);

        (await FeedAsync(stranger)).Should().BeEmpty("a stranger follows nobody");
    }

    [Fact]
    public async Task Started_gives_way_to_finished_with_result_mvp_and_level_up()
    {
        var game = await StartGameAsync(open: true);
        var follower = await _fx.LoginAsync();
        var winner = game.TeamA[0];
        await FollowAsync(follower, winner);

        await FinishGameAsync(game, mvp: winner);

        var feed = await FeedAsync(follower);
        feed.Select(Type).Should().BeEquivalentTo(["MatchEnded", "MvpAwarded", "LevelUp"],
            "\"started\" disappears once the game is over");
        var ended = feed.Single(a => Type(a) == "MatchEnded");
        ended.GetProperty("outcome").GetString().Should().Be("Win");
        ended.GetProperty("setsWon").GetInt32().Should().Be(2);
        ended.GetProperty("setsLost").GetInt32().Should().Be(0);
        var levelUp = feed.Single(a => Type(a) == "LevelUp");
        levelUp.GetProperty("levelFrom").GetInt32().Should().Be(1);
        // attendance 10 + win 15 + MVP 25 = 50 points → level 2 (20) but not 3 (60).
        levelUp.GetProperty("levelTo").GetInt32().Should().Be(2);
        levelUp.GetProperty("match").ValueKind.Should().Be(JsonValueKind.Null);
        ended.GetProperty("actor").GetProperty("level").GetInt32().Should().Be(2);

        var profile = await Read(await winner.Client.GetAsync("/api/v1/profiles/me", Ct));
        profile.GetProperty("stats").GetProperty("totalPoints").GetInt32().Should().Be(50);
        profile.GetProperty("stats").GetProperty("levelNumber").GetInt32().Should().Be(2);
        profile.GetProperty("stats").GetProperty("nextLevelPoints").GetInt32().Should().Be(60);
    }

    [Fact]
    public async Task A_private_match_never_produces_activities()
    {
        var game = await StartGameAsync(open: false);
        var follower = await _fx.LoginAsync();
        await FollowAsync(follower, game.TeamA[0]);
        await FinishGameAsync(game, mvp: game.TeamA[0]);

        (await FeedAsync(follower)).Where(a => Type(a) != "LevelUp").Should().BeEmpty();
        (await CountAsync(a => a.MatchId == game.MatchId)).Should().Be(0);
    }

    [Fact]
    public async Task A_match_that_becomes_private_disappears_from_the_feed()
    {
        var game = await StartGameAsync(open: true);
        var follower = await _fx.LoginAsync();
        await FollowAsync(follower, game.Players[0]);
        (await FeedAsync(follower)).Should().NotBeEmpty();

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE matches SET visibility = 'Private' WHERE id = {game.MatchId}", Ct);
        }

        (await FeedAsync(follower)).Should().BeEmpty();
    }

    [Fact]
    public async Task Sharing_off_hides_everything_already_recorded_and_on_shows_it_again()
    {
        var game = await StartGameAsync(open: true);
        var follower = await _fx.LoginAsync();
        var actor = game.Players[0];
        await FollowAsync(follower, actor);
        (await FeedAsync(follower)).Should().ContainSingle();

        (await Read(await actor.Client.PutAsJsonAsync("/api/v1/profiles/me/activity-sharing", new { enabled = false }, Ct)))
            .GetProperty("enabled").GetBoolean().Should().BeFalse();
        (await FeedAsync(follower)).Should().BeEmpty();
        (await Read(await actor.Client.GetAsync("/api/v1/profiles/me", Ct))).GetProperty("shareActivity").GetBoolean().Should().BeFalse();
        (await Read(await follower.Client.GetAsync($"/api/v1/profiles/{actor.Id}", Ct)))
            .GetProperty("shareActivity").ValueKind.Should().Be(JsonValueKind.Null, "the switch is the owner's business");

        await Read(await actor.Client.PutAsJsonAsync("/api/v1/profiles/me/activity-sharing", new { enabled = true }, Ct));
        (await FeedAsync(follower)).Should().ContainSingle();

        (await actor.Client.PutAsJsonAsync("/api/v1/profiles/me/activity-sharing", new { }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_repeated_event_does_not_duplicate_activities()
    {
        var game = await StartGameAsync(open: true);
        var before = await CountAsync(a => a.MatchId == game.MatchId);
        before.Should().Be(game.Players.Count);

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();
            var again = new MatchStarted(game.MatchId, game.TeamAId, game.TeamBId, "BestOf3", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await publisher.PublishAsync(again, Ct);
            await publisher.PublishAsync(again, Ct);
        }

        (await CountAsync(a => a.MatchId == game.MatchId)).Should().Be(before);
    }

    [Fact]
    public async Task Existing_history_sets_the_level_without_announcing_a_level_up()
    {
        // A player whose matches were recorded before this feature: their stats already exist.
        var veteran = await _fx.LoginAsync();
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<ProfileDbContext>().Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE player_stats SET matches_played = 40, wins = 30, mvps_received = 10 WHERE user_id = {veteran.Id}", Ct);
        }

        var stats = (await Read(await veteran.Client.GetAsync("/api/v1/profiles/me", Ct))).GetProperty("stats");
        // 40 × 10 + 30 × 15 + 10 × 25 = 1100 points = exactly level 11 (10 × 11 × 10).
        stats.GetProperty("totalPoints").GetInt32().Should().Be(1100);
        stats.GetProperty("levelNumber").GetInt32().Should().Be(11);
        (await CountAsync(a => a.ActorId == veteran.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Feed_pages_with_before_and_caps_the_limit()
    {
        var follower = await _fx.LoginAsync();
        var actor = await _fx.LoginAsync();
        await FollowAsync(follower, actor);
        var now = DateTimeOffset.UtcNow;
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var writer = scope.ServiceProvider.GetRequiredService<ActivityWriter>();
            for (var level = 2; level <= 4; level++)
            {
                await writer.RecordAsync(
                    new ActivityRecord(actor.Id, ActivityType.LevelUp, now.AddMinutes(-level), LevelFrom: level - 1, LevelTo: level), Ct);
            }

            await writer.RecordAsync(
                new ActivityRecord(actor.Id, ActivityType.LevelUp, now.AddDays(-31), LevelFrom: 1, LevelTo: 1), Ct);
        }

        var first = await Read(await follower.Client.GetAsync("/api/v1/activities?limit=2", Ct));
        first.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("levelTo").GetInt32()).Should().Equal(2, 3);
        var next = first.GetProperty("nextBefore").GetDateTimeOffset();
        var second = await Read(await follower.Client.GetAsync($"/api/v1/activities?limit=2&before={Uri.EscapeDataString(next.ToString("O"))}", Ct));
        second.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("levelTo").GetInt32()).Should().Equal(4);
        second.GetProperty("nextBefore").ValueKind.Should().Be(JsonValueKind.Null, "older than 30 days is not shown");

        (await Read(await follower.Client.GetAsync("/api/v1/activities?limit=500", Ct))).GetProperty("items").GetArrayLength().Should().Be(3);
    }

    // ── helpers ─────────────────────────────────────────────────────────────

    private sealed record Game(
        Actor Organizer,
        Guid MatchId,
        string? InviteCode,
        Guid TeamAId,
        Guid TeamBId,
        IReadOnlyList<Actor> Players,
        IReadOnlyList<Actor> TeamA);

    /// <summary>A match 10 minutes from now with 4 players, teams drawn and the game started.</summary>
    private async Task<Game> StartGameAsync(bool open)
    {
        var organizer = await _fx.LoginAsync();
        var players = new List<Actor>();
        for (var i = 0; i < 4; i++)
        {
            players.Add(await _fx.LoginAsync());
        }

        var match = await Read(await organizer.Client.PostAsJsonAsync("/api/v1/matches", new
        {
            name = "Racha da Galera", address = "Arena", latitude = -23.5, longitude = -46.6,
            dateTime = DateTimeOffset.UtcNow.AddMinutes(10), maxPlayers = 12, regularSlots = 12, type = "OneOff",
            visibility = open ? "Open" : "Private", inviteMode = open ? null : "Code",
        }, Ct), HttpStatusCode.Created);
        var matchId = match.GetProperty("id").GetGuid();
        var b = $"/api/v1/matches/{matchId}";
        var code = open ? null : (await Read(await organizer.Client.GetAsync($"{b}/detail", Ct))).GetProperty("inviteCode").GetString();
        foreach (var player in players)
        {
            await Read(await player.Client.PutAsJsonAsync($"{b}/presences/me", new { status = "Confirmed", inviteCode = code }, Ct));
        }

        var teams = (await Read(await organizer.Client.PostAsJsonAsync($"{b}/teams/draft", new { teamCount = 2, mode = "Random" }, Ct)))
            .GetProperty("teams").EnumerateArray().ToList();
        var teamA = teams[0].GetProperty("id").GetGuid();
        var teamB = teams[1].GetProperty("id").GetGuid();
        var teamAIds = teams[0].GetProperty("members").EnumerateArray().Select(m => m.GetProperty("playerId").GetGuid()).ToHashSet();
        await Read(await organizer.Client.PostAsJsonAsync($"{b}/scoreboard", new { format = "BestOf3", teamAId = teamA, teamBId = teamB }, Ct),
            HttpStatusCode.Created);
        await Read(await organizer.Client.PostAsync($"{b}/scoreboard/start", null, Ct));

        return new Game(organizer, matchId, code, teamA, teamB, players, players.Where(p => teamAIds.Contains(p.Id)).ToList());
    }

    /// <summary>Team A wins 2–0, <paramref name="mvp"/> is voted MVP, and the summary is generated.</summary>
    private async Task FinishGameAsync(Game game, Actor mvp)
    {
        var b = $"/api/v1/matches/{game.MatchId}";
        for (var set = 1; set <= 2; set++)
        {
            for (var i = 0; i < 25; i++)
            {
                await Read(await game.Organizer.Client.PostAsJsonAsync($"{b}/scoreboard/sets/{set}/points", new { teamId = game.TeamAId }, Ct));
            }
        }

        (await game.Organizer.Client.PostAsJsonAsync($"{b}/mvp-voting", new { }, Ct)).StatusCode
            .Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.Conflict);
        var voter = game.Players.First(p => p.Id != mvp.Id);
        await Read(await voter.Client.PostAsJsonAsync($"{b}/mvp-voting/votes", new { votedPlayerId = mvp.Id }, Ct));
        await Read(await game.Organizer.Client.PostAsync($"{b}/mvp-voting/close", null, Ct));
        await Read(await game.Organizer.Client.PostAsync($"{b}/summary", null, Ct), HttpStatusCode.Created);
    }

    private static async Task FollowAsync(Actor follower, Actor followed) =>
        await Read(await follower.Client.PutAsync($"/api/v1/profiles/{followed.Id}/follow", null, Ct));

    private static async Task<List<JsonElement>> FeedAsync(Actor caller) =>
        (await Read(await caller.Client.GetAsync("/api/v1/activities", Ct))).GetProperty("items").EnumerateArray().ToList();

    private async Task<int> CountAsync(System.Linq.Expressions.Expression<Func<Activity, bool>> where)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ProfileDbContext>().Activities.CountAsync(where, Ct);
    }

    private static string? Type(JsonElement activity) => activity.GetProperty("type").GetString();

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(expected, text);
        return string.IsNullOrEmpty(text) ? default : JsonDocument.Parse(text).RootElement.Clone();
    }
}
