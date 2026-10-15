using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// A game (and its ranking points) cannot be recorded before the match happens (audit SEC-06):
/// the scoreboard is refused until 15 minutes before the match time.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class KickoffAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public KickoffAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task R6_points_are_not_credited_before_the_match_happens()
    {
        var organizer = await _fx.LoginAsync();
        var kickoff = DateTimeOffset.UtcNow.AddDays(6);
        var matchId = await CreateRecurringMatchAsync(organizer, kickoff, opensHoursBefore: 160);
        var players = new List<Actor> { organizer };
        for (var i = 0; i < 3; i++)
        {
            players.Add(await _fx.LoginAsync());
        }

        foreach (var who in players)
        {
            await Ok(who.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct));
        }

        var b = $"/api/v1/matches/{matchId}";
        var teams = (await Read(await organizer.Client.PostAsJsonAsync($"{b}/teams/draft", new { teamCount = 2, mode = "Random" }, Ct)))
            .GetProperty("teams").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToArray();

        var create = await organizer.Client.PostAsJsonAsync(
            $"{b}/scoreboard", new { format = "BestOf3", teamAId = teams[0], teamBId = teams[1] }, Ct);
        var problem = await Read(create, HttpStatusCode.UnprocessableEntity);
        problem.GetProperty("startsAt").GetDateTimeOffset().Should().BeCloseTo(kickoff, TimeSpan.FromSeconds(1));

        (await organizer.Client.PostAsync($"{b}/scoreboard/start", null, Ct)).IsSuccessStatusCode.Should().BeFalse();
        (await organizer.Client.PostAsJsonAsync($"{b}/scoreboard/sets/1/points", new { teamId = teams[0] }, Ct))
            .IsSuccessStatusCode.Should().BeFalse();

        var ranking = await organizer.Client.GetAsync($"{b}/ranking", Ct);
        if (ranking.StatusCode == HttpStatusCode.OK)
        {
            (await Read(ranking)).GetProperty("items").EnumerateArray().Should().BeEmpty(
                $"the match only starts on {kickoff:yyyy-MM-dd HH:mm} UTC, yet points were credited");
        }
    }

    [Fact]
    public async Task Control_scoreboard_is_allowed_within_the_tolerance_before_kickoff()
    {
        var organizer = await _fx.LoginAsync();
        var player = await _fx.LoginAsync();
        var matchId = await CreateRecurringMatchAsync(organizer, DateTimeOffset.UtcNow.AddMinutes(10), opensHoursBefore: 24);
        foreach (var who in new[] { organizer, player })
        {
            await Ok(who.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct));
        }

        var b = $"/api/v1/matches/{matchId}";
        var teams = (await Read(await organizer.Client.PostAsJsonAsync($"{b}/teams/draft", new { teamCount = 2, mode = "Random" }, Ct)))
            .GetProperty("teams").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToArray();
        await Read(await organizer.Client.PostAsJsonAsync($"{b}/scoreboard", new { format = "BestOf3", teamAId = teams[0], teamBId = teams[1] }, Ct),
            HttpStatusCode.Created);
        await Ok(organizer.Client.PostAsync($"{b}/scoreboard/start", null, Ct));
    }

    private async Task<Guid> CreateRecurringMatchAsync(Actor organizer, DateTimeOffset when, int opensHoursBefore)
    {
        var response = await organizer.Client.PostAsJsonAsync("/api/v1/matches", new
        {
            name = "Racha", address = "Rua A", latitude = -23.5, longitude = -46.6, dateTime = when,
            maxPlayers = 12, regularSlots = 12, type = "Recurring", frequency = "Weekly",
            dayOfWeek = ((int)when.DayOfWeek + 6) % 7 + 1, confirmationOpensHoursBefore = opensHoursBefore,
        }, Ct);
        return (await Read(response, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private static async Task Ok(Task<HttpResponseMessage> call)
    {
        var response = await call;
        response.IsSuccessStatusCode.Should().BeTrue(await response.Content.ReadAsStringAsync(Ct));
    }

    private static async Task<JsonElement> Read(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var text = await response.Content.ReadAsStringAsync(Ct);
        response.StatusCode.Should().Be(expected, text);
        return JsonDocument.Parse(text).RootElement.Clone();
    }
}
