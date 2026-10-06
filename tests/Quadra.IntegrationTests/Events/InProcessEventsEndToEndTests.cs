using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Quadra.IntegrationTests.Modules.Auth;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Testcontainers.PostgreSql;

namespace Quadra.IntegrationTests.Events;

/// <summary>
/// End-to-end test of the host exactly as it is deployed: the REAL event publisher (in-process
/// handlers — no queue, no worker), migrations applied on startup, and no object storage
/// configured. Nothing is substituted except the SMS provider (the fake with a fixed code).
///
/// It proves the two things a queue-less, storage-less deployment depends on:
///  - logging in for the first time provisions the player profile (UserRegistered → Profile);
///  - generating a match summary updates player stats and the group ranking
///    (MatchSummaryGenerated → Profile + Gamification);
/// and that photos are optional: profiles are served without a photo and an upload request
/// answers 503 instead of breaking the API.
/// </summary>
public sealed class InProcessEventsEndToEndTests : IClassFixture<InProcessEventsEndToEndTests.Fixture>
{
    private readonly Fixture _fx;

    public InProcessEventsEndToEndTests(Fixture fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>
    /// Covers: FA.3 + F2.1 — the first login creates the account AND its profile, with no worker
    /// involved; the profile has no photo; a photo upload is refused with 503.
    /// </summary>
    [Fact]
    public async Task First_login_provisions_the_profile_and_photos_are_optional()
    {
        var player = await LoginAsync("+5511911110001");

        var profile = await player.Client.GetAsync("/api/v1/profiles/me", Ct);

        profile.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await profile.Content.ReadFromJsonAsync<JsonElement>(Ct);
        body.GetProperty("userId").GetGuid().Should().Be(player.UserId);
        body.GetProperty("photoUrl").ValueKind.Should().Be(JsonValueKind.Null);
        body.GetProperty("stats").GetProperty("matchesPlayed").GetInt32().Should().Be(0);

        var upload = await player.Client.PostAsJsonAsync(
            "/api/v1/profiles/me/photo/upload-url", new { ContentType = "image/jpeg" }, Ct);
        upload.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    /// <summary>
    /// Covers: F1.6 → F2.1 + F2.3 — once the organizer generates the summary of a finished
    /// recurring match, every participant's stats and the group ranking are updated in-process.
    /// </summary>
    [Fact]
    public async Task Generating_a_match_summary_updates_player_stats_and_the_group_ranking()
    {
        var organizer = await LoginAsync("+5511922220000");
        var players = new[]
        {
            await LoginAsync("+5511922220001"),
            await LoginAsync("+5511922220002"),
            await LoginAsync("+5511922220003"),
            await LoginAsync("+5511922220004"),
        };
        var mvp = players[1];

        var matchId = await CreateRecurringMatchAsync(organizer);
        foreach (var player in players)
        {
            await AddConfirmedPresenceAsync(matchId, player.UserId);
        }

        await ForceMatchStatusAsync(matchId, "Closed");

        (await organizer.Client.PostAsync($"/api/v1/matches/{matchId}/teams/draft", null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        var (teamAId, teamAPlayers) = await GetTeamAAsync(organizer, matchId);

        // Team A wins a best-of-3 by 2 sets to 0.
        (await organizer.Client.PostAsJsonAsync($"/api/v1/matches/{matchId}/scoreboard", new { Format = "BestOf3" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await organizer.Client.PostAsync($"/api/v1/matches/{matchId}/scoreboard/start", null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        foreach (var setNumber in new[] { 1, 2 })
        {
            for (var point = 0; point < 25; point++)
            {
                (await organizer.Client.PostAsJsonAsync(
                    $"/api/v1/matches/{matchId}/scoreboard/sets/{setNumber}/points", new { TeamId = teamAId }, Ct))
                    .StatusCode.Should().Be(HttpStatusCode.OK);
            }
        }

        // Everyone votes for the same MVP (who votes for someone else).
        // The voting was opened by the game ending (MatchEnded handled in-process): opening it
        // again by hand is refused.
        (await organizer.Client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/mvp-voting", new { DeadlineAt = (DateTimeOffset?)null }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);
        foreach (var voter in players)
        {
            var voted = voter == mvp ? players[0] : mvp;
            (await voter.Client.PostAsJsonAsync(
                $"/api/v1/matches/{matchId}/mvp-voting/votes", new { VotedPlayerId = voted.UserId }, Ct))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        (await organizer.Client.PostAsync($"/api/v1/matches/{matchId}/mvp-voting/close", null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        // The trigger: MatchSummaryGenerated is published and handled inside this request.
        (await organizer.Client.PostAsync($"/api/v1/matches/{matchId}/summary", null, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        // Profile stats: winners got a win, losers a loss, the MVP an MVP award.
        foreach (var player in players)
        {
            var stats = (await GetJsonAsync(player, "/api/v1/profiles/me")).GetProperty("stats");
            var won = teamAPlayers.Contains(player.UserId);

            stats.GetProperty("matchesPlayed").GetInt32().Should().Be(1);
            stats.GetProperty("wins").GetInt32().Should().Be(won ? 1 : 0);
            stats.GetProperty("losses").GetInt32().Should().Be(won ? 0 : 1);
            stats.GetProperty("mvpsReceived").GetInt32().Should().Be(player == mvp ? 1 : 0);
        }

        var history = await GetJsonAsync(mvp, "/api/v1/profiles/me/match-history");
        history.GetProperty("items").GetArrayLength().Should().Be(1);
        // The history row carries the set score from the player's side.
        var row = history.GetProperty("items")[0];
        var (setsWon, setsLost) = (row.GetProperty("setsWon").GetInt32(), row.GetProperty("setsLost").GetInt32());
        (setsWon + setsLost).Should().BeGreaterThanOrEqualTo(2);
        (setsWon > setsLost).Should().Be(teamAPlayers.Contains(mvp.UserId));

        // Group ranking: all four participants are ranked with points from this match.
        var ranking = await GetJsonAsync(organizer, $"/api/v1/matches/{matchId}/ranking");
        var entries = ranking.GetProperty("items").EnumerateArray().ToList();
        entries.Select(e => e.GetProperty("userId").GetGuid())
            .Should().BeEquivalentTo(players.Select(p => p.UserId));
        entries.Should().OnlyContain(e =>
            e.GetProperty("totalPoints").GetInt32() > 0 && e.GetProperty("matchesCounted").GetInt32() == 1);

        // "My ranking" finds that group without naming the match, with player names.
        var mine = await GetJsonAsync(mvp, "/api/v1/rankings/mine?pageSize=4");
        mine.GetProperty("groupId").GetGuid().Should().Be(matchId);
        mine.GetProperty("items").EnumerateArray()
            .Should().HaveCount(4).And.OnlyContain(e => !string.IsNullOrEmpty(e.GetProperty("displayName").GetString()));
        mine.GetProperty("callerEntry").GetProperty("userId").GetGuid().Should().Be(mvp.UserId);

        // Someone who never scored is in no ranking.
        var newcomer = await LoginAsync("+5511977770099");
        (await newcomer.Client.GetAsync("/api/v1/rankings/mine", Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private sealed record LoggedInPlayer(Guid UserId, HttpClient Client);

    /// <summary>Logs a phone number in through the real endpoint (fake SMS code).</summary>
    private async Task<LoggedInPlayer> LoginAsync(string phoneNumber)
    {
        var client = _fx.Factory.CreateClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/auth/login/sms-otp",
            new { step = "verify", phoneNumber, code = TestJwtFactory.FakeOtpCode },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", body.GetProperty("accessToken").GetString());
        return new LoggedInPlayer(body.GetProperty("userId").GetGuid(), client);
    }

    private static async Task<JsonElement> GetJsonAsync(LoggedInPlayer player, string url)
    {
        var response = await player.Client.GetAsync(url, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK, "GET {0} should succeed", url);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private static async Task<Guid> CreateRecurringMatchAsync(LoggedInPlayer organizer)
    {
        var dateTime = DateTimeOffset.UtcNow.AddDays(30);
        var response = await organizer.Client.PostAsJsonAsync(
            "/api/v1/matches",
            new
            {
                Name = "Racha de quinta",
                Description = (string?)null,
                Address = "Rua Teste, 123, São Paulo",
                Latitude = -23.5505,
                Longitude = -46.6333,
                DateTime = dateTime,
                MaxPlayers = 12,
                RegularSlots = 8,
                Price = (decimal?)null,
                Type = "Recurring",
                Frequency = "Weekly",
                // 1 (Monday) … 7 (Sunday).
                DayOfWeek = dateTime.DayOfWeek == System.DayOfWeek.Sunday ? 7 : (int)dateTime.DayOfWeek,
                WindowOpensAt = DateTimeOffset.UtcNow.AddDays(5),
                WindowClosesAt = DateTimeOffset.UtcNow.AddDays(15),
            },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private async Task AddConfirmedPresenceAsync(Guid matchId, Guid playerId)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var now = DateTimeOffset.UtcNow;
        var presence = MatchPresence.Create(matchId, playerId, PlayerType.Regular, now);
        presence.Confirm(now);
        ctx.Presences.Add(presence);
        await ctx.SaveChangesAsync(Ct);
    }

    private async Task ForceMatchStatusAsync(Guid matchId, string status)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await ctx.Database.ExecuteSqlRawAsync("UPDATE matches SET status = {0} WHERE id = {1}", status, matchId);
    }

    private static async Task<(Guid TeamAId, HashSet<Guid> PlayerIds)> GetTeamAAsync(
        LoggedInPlayer organizer,
        Guid matchId)
    {
        var body = await GetJsonAsync(organizer, $"/api/v1/matches/{matchId}/teams");
        var teamA = body.GetProperty("teams").EnumerateArray()
            .First(t => t.GetProperty("name").GetString() == "Team A");

        var playerIds = teamA.GetProperty("members").EnumerateArray()
            .Select(m => m.GetProperty("playerId").GetGuid())
            .ToHashSet();

        return (teamA.GetProperty("id").GetGuid(), playerIds);
    }

    // ── fixture ──────────────────────────────────────────────────────────────

    public sealed class Fixture : IAsyncLifetime
    {
        private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgis/postgis:16-3.4")
            .Build();

        public WebApplicationFactory<Program> Factory { get; private set; } = default!;

        public async ValueTask InitializeAsync()
        {
            await _postgres.StartAsync();

            // Deliberately NO ConfigureTestServices: the real publisher, handlers and the
            // unconfigured photo storage are what is under test.
            Factory = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.UseEnvironment("Testing");
                    builder.ConfigureAppConfiguration((_, config) =>
                    {
                        config.AddInMemoryCollection(TestJwtFactory.AuthSettings());
                        config.AddInMemoryCollection(new Dictionary<string, string?>
                        {
                            ["ConnectionStrings:Default"] = _postgres.GetConnectionString(),
                            ["Database:MigrateOnStartup"] = "true",
                        });
                    });
                });

            // Building the host runs the startup migrations for every module.
            using var client = Factory.CreateClient();
            (await client.GetAsync("/health")).EnsureSuccessStatusCode();
        }

        public async ValueTask DisposeAsync()
        {
            await Factory.DisposeAsync();
            await _postgres.DisposeAsync();
        }
    }
}
