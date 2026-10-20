using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Api.Background;

namespace Quadra.IntegrationTests.SecurityAudit;

/// <summary>
/// Essential notifications (DECISIONS #77): the waiting list spot and the "tomorrow" reminder reach
/// the right players once; each player reads and marks only their own; push token and preferences
/// are validated.
/// </summary>
[Trait("Category", "SecurityAudit")]
public sealed class NotificationsAuditTests : IClassFixture<AuditWebApplicationFactory>
{
    private readonly AuditWebApplicationFactory _fx;

    public NotificationsAuditTests(AuditWebApplicationFactory fx) => _fx = fx;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task<Guid> CreateMatchAsync(Actor organizer, DateTimeOffset when, int slots)
    {
        var response = await organizer.Client.PostAsJsonAsync("/api/v1/matches", new
        {
            name = "Racha de Teste", address = "Rua A", latitude = -23.5, longitude = -46.6, dateTime = when,
            maxPlayers = Math.Max(2, slots), regularSlots = slots, type = "OneOff", confirmationOpensHoursBefore = 96,
        }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetGuid();
    }

    private static Task<HttpResponseMessage> Confirm(Actor who, Guid matchId, string status = "Confirmed") =>
        who.Client.PutAsJsonAsync($"/api/v1/matches/{matchId}/presences/me", new { status }, Ct);

    private static async Task<JsonElement[]> ListAsync(Actor who) =>
        (await who.Client.GetFromJsonAsync<JsonElement>("/api/v1/notifications", Ct)).GetProperty("items").EnumerateArray().ToArray();

    private static async Task<int> UnreadAsync(Actor who) =>
        (await who.Client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/unread-count", Ct)).GetProperty("count").GetInt32();

    [Fact]
    public async Task A_spot_from_the_waiting_list_is_notified_to_that_player_only()
    {
        var organizer = await _fx.LoginAsync();
        var waiter = await _fx.LoginAsync();
        var stranger = await _fx.LoginAsync();
        var matchId = await CreateMatchAsync(organizer, DateTimeOffset.UtcNow.AddDays(3), slots: 2);
        (await Confirm(organizer, matchId)).IsSuccessStatusCode.Should().BeTrue();
        (await Confirm(stranger, matchId)).IsSuccessStatusCode.Should().BeTrue();
        (await Confirm(waiter, matchId)).StatusCode.Should().Be(HttpStatusCode.Conflict, "the match is full: waiting list");

        (await Confirm(stranger, matchId, "Declined")).IsSuccessStatusCode.Should().BeTrue();

        var mine = await ListAsync(waiter);
        mine.Should().ContainSingle(n => n.GetProperty("type").GetString() == "WaitingListSpot");
        mine[0].GetProperty("matchId").GetGuid().Should().Be(matchId);
        (await UnreadAsync(waiter)).Should().Be(1);
        (await ListAsync(organizer)).Should().NotContain(n => n.GetProperty("type").GetString() == "WaitingListSpot");

        var id = mine[0].GetProperty("id").GetGuid();
        (await organizer.Client.PostAsync($"/api/v1/notifications/{id}/read", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NotFound,
            "a player cannot touch someone else's notification");
        (await waiter.Client.PostAsync($"/api/v1/notifications/{id}/read", null, Ct)).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await UnreadAsync(waiter)).Should().Be(0);
    }

    [Fact]
    public async Task The_reminder_reaches_pending_and_confirmed_players_once()
    {
        var organizer = await _fx.LoginAsync();
        var player = await _fx.LoginAsync();
        var matchId = await CreateMatchAsync(organizer, DateTimeOffset.UtcNow.AddHours(10), slots: 6);
        (await Confirm(organizer, matchId)).IsSuccessStatusCode.Should().BeTrue();
        (await organizer.Client.PostAsJsonAsync($"/api/v1/matches/{matchId}/presences", new { playerId = player.Id, playerType = "Regular" }, Ct))
            .IsSuccessStatusCode.Should().BeTrue("the organizer adds a pending player");

        var sweeper = _fx.Factory.Services.GetRequiredService<MatchReminderSweeper>();
        await sweeper.SweepOnceAsync(Ct);
        await sweeper.SweepOnceAsync(Ct);

        (await ListAsync(organizer)).Where(n => n.GetProperty("matchId").ValueKind != JsonValueKind.Null
                && n.GetProperty("matchId").GetGuid() == matchId)
            .Select(n => n.GetProperty("type").GetString()).Should().Equal("MatchTomorrow");
        (await ListAsync(player)).Where(n => n.GetProperty("matchId").GetGuid() == matchId)
            .Select(n => n.GetProperty("type").GetString()).Should().Equal("ConfirmPresence");
    }

    [Fact]
    public async Task Push_token_and_preferences_are_validated_and_kept_per_account()
    {
        var user = await _fx.LoginAsync();
        (await user.Client.PutAsJsonAsync("/api/v1/notifications/push-token", new { token = "not-a-token" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await user.Client.PutAsJsonAsync("/api/v1/notifications/push-token", new { token = "ExponentPushToken[abc123]" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await user.Client.PutAsJsonAsync("/api/v1/notifications/preferences", new { invites = true, reminders = false, ranking = true }, Ct))
            .IsSuccessStatusCode.Should().BeTrue();
        var prefs = await user.Client.GetFromJsonAsync<JsonElement>("/api/v1/notifications/preferences", Ct);
        prefs.GetProperty("reminders").GetBoolean().Should().BeFalse();

        (await _fx.Factory.CreateClient().GetAsync("/api/v1/notifications", Ct)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
