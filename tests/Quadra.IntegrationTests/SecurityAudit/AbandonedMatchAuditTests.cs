using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Api.Background;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// DECISIONS #104: whoever creates a match starts confirmed, and a match nobody closed is closed
/// by the sweep once it is <c>Matches:AbandonAfterHours</c> past its time: cancelled when never
/// started, ended and summarized when left on the scoreboard. Recent matches are left alone.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class AbandonedMatchAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public AbandonedMatchAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_organizer_starts_confirmed()
    {
        var organizer = await _fx.LoginAsync();
        var matchId = await CreateMatchAsync(organizer, DateTimeOffset.UtcNow.AddDays(2));

        await using var scope = _fx.Factory.Services.CreateAsyncScope();
        var presences = await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Presences
            .AsNoTracking().Where(p => p.MatchId == matchId).ToListAsync(Ct);
        presences.Should().ContainSingle(p => p.PlayerId == organizer.Id && p.Status == PresenceStatus.Confirmed);
    }

    [Fact]
    public async Task A_match_never_started_is_cancelled_and_a_recent_one_is_kept()
    {
        var organizer = await _fx.LoginAsync();
        var abandoned = await CreateMatchAsync(organizer, DateTimeOffset.UtcNow.AddDays(2));
        var recent = await CreateMatchAsync(organizer, DateTimeOffset.UtcNow.AddDays(2));
        await MoveToAsync(abandoned, DateTimeOffset.UtcNow.AddHours(-13));
        await MoveToAsync(recent, DateTimeOffset.UtcNow.AddHours(-2));

        await _fx.Factory.Services.GetRequiredService<AbandonedMatchSweeper>().SweepOnceAsync(Ct);

        (await StatusAsync(abandoned)).Should().Be(MatchStatus.Cancelled);
        (await StatusAsync(recent)).Should().NotBe(MatchStatus.Cancelled);
    }

    [Fact]
    public async Task A_game_left_on_the_scoreboard_is_ended_and_summarized()
    {
        var organizer = await _fx.LoginAsync();
        var player = await _fx.LoginAsync();
        var matchId = await CreateMatchAsync(organizer, DateTimeOffset.UtcNow.AddMinutes(10));
        await Ok(player.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed" }, Ct));

        var b = $"/api/v1/matches/{matchId}";
        var teams = (await Read(await organizer.Client.PostAsJsonAsync($"{b}/teams/draft", new { teamCount = 2, mode = "Random" }, Ct)))
            .GetProperty("teams").EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).ToArray();
        await Read(await organizer.Client.PostAsJsonAsync($"{b}/scoreboard", new { teamAId = teams[0], teamBId = teams[1] }, Ct),
            HttpStatusCode.Created);
        await Ok(organizer.Client.PostAsync($"{b}/scoreboard/start", null, Ct));
        await Ok(organizer.Client.PostAsJsonAsync($"{b}/scoreboard/sets/1/points", new { teamId = teams[0] }, Ct));
        await MoveToAsync(matchId, DateTimeOffset.UtcNow.AddHours(-13));

        await _fx.Factory.Services.GetRequiredService<AbandonedMatchSweeper>().SweepOnceAsync(Ct);

        (await StatusAsync(matchId)).Should().Be(MatchStatus.Ended);
        (await organizer.Client.GetAsync($"{b}/summary", Ct)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private async Task<Guid> CreateMatchAsync(Actor organizer, DateTimeOffset when)
    {
        var response = await organizer.Client.PostAsJsonAsync("/api/v1/matches", new
        {
            name = "Racha esquecido", address = "Rua A", latitude = -23.5, longitude = -46.6, dateTime = when,
            maxPlayers = 8, regularSlots = 8, type = "OneOff", confirmationOpensHoursBefore = 96,
        }, Ct);
        return (await Read(response, HttpStatusCode.Created)).GetProperty("id").GetGuid();
    }

    private async Task MoveToAsync(Guid matchId, DateTimeOffset when)
    {
        await using var scope = _fx.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Matches
            .Where(m => m.Id == matchId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.DateTime, when.ToUniversalTime()), Ct);
    }

    private async Task<MatchStatus> StatusAsync(Guid matchId)
    {
        await using var scope = _fx.Factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<MatchesDbContext>().Matches
            .AsNoTracking().Where(m => m.Id == matchId).Select(m => m.Status).SingleAsync(Ct);
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
