using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;

namespace Quadra.IntegrationTests.Matches;

/// <summary>
/// HTTP integration tests for presence management endpoints (F1.2).
///
/// Covers acceptance criteria:
///   - AC-P1: GET /api/v1/matches/{id}/presences returns presences partitioned into
///             Confirmed, Pending, Declined lists plus WaitingList.
///   - AC-P2: OpenMatchWindowHandler publishes MatchWindowOpened SQS event after Draft → Open.
///   - AC-P3: When a player confirms and all slots are taken, a row is inserted in waiting_list
///             and the response returns HTTP 409 with the waiting list position.
///   - AC-P4: After windowClosesAt, a DropIn player who confirms when all original DropInSlots
///             are taken but at least one Regular slot was released receives HTTP 200.
/// </summary>
public sealed class PresenceEndpointsTests : IClassFixture<MatchesWebApplicationFactory>
{
    private readonly MatchesWebApplicationFactory _fx;
    private static readonly Guid OrganizerUserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    public PresenceEndpointsTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        // CASCADE takes care of presences and waiting_list rows.
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE waiting_list, match_presences, matches RESTART IDENTITY CASCADE;");
        _fx.Publisher.ClearReceivedCalls();
    }

    // ─── Helper: create a match via API ──────────────────────────────────────

    private async Task<Guid> CreateMatchAsync(
        Guid organizerId,
        int maxPlayers = 4,
        int regularSlots = 2)
    {
        var client = _fx.CreateAuthenticatedClient(organizerId);
        var payload = BuildMatchPayload(maxPlayers, regularSlots);
        var response = await client.PostAsJsonAsync(
            "/api/v1/matches", payload, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    /// <summary>
    /// Adds a presence row directly in DB (bypasses window-check business rules).
    /// Used to set up test state that cannot be reached through the API in a controlled test environment.
    /// </summary>
    private async Task<Guid> AddPresenceDirectlyAsync(
        Guid matchId,
        Guid playerId,
        PlayerType playerType,
        PresenceStatus status = PresenceStatus.Pending)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var now = DateTimeOffset.UtcNow;
        var presence = MatchPresence.Create(matchId, playerId, playerType, now);

        if (status == PresenceStatus.Confirmed) presence.Confirm(now);
        else if (status == PresenceStatus.Declined) presence.Decline(now);

        ctx.Presences.Add(presence);
        await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        return presence.Id;
    }

    private async Task ForceMatchStatusAsync(Guid matchId, MatchStatus status,
        short? releasedDropInSlots = null)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();

        if (releasedDropInSlots.HasValue)
        {
            await ctx.Database.ExecuteSqlAsync(
                $"UPDATE matches SET status = {status.ToString()}, released_drop_in_slots = {releasedDropInSlots.Value} WHERE id = {matchId}");
        }
        else
        {
            await ctx.Database.ExecuteSqlAsync(
                $"UPDATE matches SET status = {status.ToString()} WHERE id = {matchId}");
        }
    }

    // ─── AC-P1: GET /presences returns partitioned lists ─────────────────────

    /// <summary>
    /// Covers: AC-P1 — GET /api/v1/matches/{id}/presences returns presences partitioned into
    /// Confirmed, Pending, Declined lists plus WaitingList, with correct ConfirmedCount, RegularSlots, DropInSlots.
    /// </summary>
    [Fact]
    public async Task GET_presences_returns_presences_partitioned_into_all_buckets_with_waiting_list()
    {
        await ResetAsync();

        var matchId = await CreateMatchAsync(OrganizerUserId, maxPlayers: 4, regularSlots: 2);

        var confirmedPlayerId = Guid.NewGuid();
        var pendingPlayerId = Guid.NewGuid();
        var declinedPlayerId = Guid.NewGuid();

        await AddPresenceDirectlyAsync(matchId, confirmedPlayerId, PlayerType.Regular, PresenceStatus.Confirmed);
        await AddPresenceDirectlyAsync(matchId, pendingPlayerId, PlayerType.Regular, PresenceStatus.Pending);
        await AddPresenceDirectlyAsync(matchId, declinedPlayerId, PlayerType.Regular, PresenceStatus.Declined);

        // Add a waiting list entry directly in DB.
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
            var waiterEntry = WaitingListEntry.Create(matchId, Guid.NewGuid(), PlayerType.Regular, 1, DateTimeOffset.UtcNow);
            ctx.WaitingList.Add(waiterEntry);
            await ctx.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync(
            $"/api/v1/matches/{matchId}/presences", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        var confirmed = body.GetProperty("confirmed");
        var pending = body.GetProperty("pending");
        var declined = body.GetProperty("declined");
        var waitingList = body.GetProperty("waitingList");

        confirmed.GetArrayLength().Should().Be(1);
        pending.GetArrayLength().Should().Be(1);
        declined.GetArrayLength().Should().Be(1);
        waitingList.GetArrayLength().Should().Be(1);

        body.GetProperty("confirmedCount").GetInt32().Should().Be(1);
        body.GetProperty("regularSlots").GetInt32().Should().Be(2);
        body.GetProperty("dropInSlots").GetInt32().Should().Be(2); // maxPlayers(4) - regularSlots(2)

        // Verify statuses in the response buckets.
        confirmed[0].GetProperty("status").GetString().Should().Be("Confirmed");
        pending[0].GetProperty("status").GetString().Should().Be("Pending");
        declined[0].GetProperty("status").GetString().Should().Be("Declined");

        // Verify waiting list entry structure.
        waitingList[0].GetProperty("position").GetInt32().Should().Be(1);
        waitingList[0].GetProperty("playerType").GetString().Should().Be("Regular");
    }

    /// <summary>
    /// Covers: GET /presences returns 404 when match does not exist.
    /// </summary>
    [Fact]
    public async Task GET_presences_returns_404_for_non_existent_match()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync(
            $"/api/v1/matches/{Guid.NewGuid()}/presences", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers: GET /presences returns 401 when not authenticated.
    /// </summary>
    [Fact]
    public async Task GET_presences_returns_401_when_unauthenticated()
    {
        var matchId = Guid.NewGuid();
        var client = _fx.CreateAnonymousClient();
        var response = await client.GetAsync(
            $"/api/v1/matches/{matchId}/presences", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── AC-P2: OpenMatchWindowHandler publishes MatchWindowOpened ───────────

    /// <summary>
    /// Covers: AC-P2 — After a match transitions from Draft to Open via OpenMatchWindowHandler,
    /// the MatchWindowOpened SQS event is published with the correct payload.
    ///
    /// We invoke the handler directly (it's scoped in DI) to simulate what the Background Worker
    /// would call. The integration test verifies the event is received by the substitute publisher
    /// and that the match status is persisted as Open.
    /// </summary>
    [Fact]
    public async Task OpenMatchWindowHandler_publishes_MatchWindowOpened_event_and_persists_Open_status()
    {
        await ResetAsync();

        var matchId = await CreateMatchAsync(OrganizerUserId);

        // Add two pending presences so the event includes PendingPlayerIds.
        var player1 = Guid.NewGuid();
        var player2 = Guid.NewGuid();
        await AddPresenceDirectlyAsync(matchId, player1, PlayerType.Regular, PresenceStatus.Pending);
        await AddPresenceDirectlyAsync(matchId, player2, PlayerType.Regular, PresenceStatus.Pending);

        // Invoke the handler directly — simulates the Background Worker trigger.
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var handler = scope.ServiceProvider.GetRequiredService<Quadra.Modules.Matches.Application.OpenMatchWindowHandler>();
            await handler.HandleAsync(matchId, TestContext.Current.CancellationToken);
        }

        // Verify match status persisted as Open.
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
            var match = await ctx.Matches.SingleAsync(
                m => m.Id == matchId, TestContext.Current.CancellationToken);
            match.Status.Should().Be(MatchStatus.Open);
        }

        // Verify MatchWindowOpened event was published with both pending player IDs.
        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<MatchWindowOpened>(e =>
                e.MatchId == matchId
                && e.OrganizerId == OrganizerUserId
                && e.PendingPlayerIds.Count == 2
                && e.PendingPlayerIds.Contains(player1)
                && e.PendingPlayerIds.Contains(player2)),
            Arg.Any<CancellationToken>());
    }

    // ─── AC-P3: slot full → 409 with waiting list position ───────────────────

    /// <summary>
    /// Covers: AC-P3 — PUT /api/v1/matches/{id}/presences/me returns 409 with waitingListPosition
    /// when all RegularSlots are already confirmed, and the player is added to waiting_list.
    /// </summary>
    [Fact]
    public async Task PUT_me_returns_409_with_waitingListPosition_when_all_regular_slots_are_full()
    {
        await ResetAsync();

        // Match with 2 regular slots.
        var matchId = await CreateMatchAsync(OrganizerUserId, maxPlayers: 4, regularSlots: 2);

        // Force match to Open status so the window eligibility check passes.
        await ForceMatchStatusAsync(matchId, MatchStatus.Open);

        // Force WindowOpensAt in the past via direct DB update.
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
            await ctx.Database.ExecuteSqlAsync(
                $"UPDATE matches SET window_opens_at = {DateTimeOffset.UtcNow.AddDays(-1)} WHERE id = {matchId}",
                TestContext.Current.CancellationToken);
        }

        // Add the player who will attempt to confirm.
        var confirmingPlayerId = Guid.NewGuid();
        await AddPresenceDirectlyAsync(matchId, confirmingPlayerId, PlayerType.Regular, PresenceStatus.Pending);

        // Fill both Regular slots with other confirmed players.
        var fullPlayer1 = Guid.NewGuid();
        var fullPlayer2 = Guid.NewGuid();
        await AddPresenceDirectlyAsync(matchId, fullPlayer1, PlayerType.Regular, PresenceStatus.Confirmed);
        await AddPresenceDirectlyAsync(matchId, fullPlayer2, PlayerType.Regular, PresenceStatus.Confirmed);

        var client = _fx.CreateAuthenticatedClient(confirmingPlayerId);
        var response = await client.PutAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences/me",
            new { status = "Confirmed" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);

        // The response body must include the waitingListPosition extension.
        body.TryGetProperty("waitingListPosition", out var positionElement).Should().BeTrue(
            "response body must contain waitingListPosition extension");
        positionElement.GetInt32().Should().BeGreaterThan(0);

        // Verify waiting list entry was persisted.
        using var verifyScope = _fx.Factory.Services.CreateScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var waitingEntry = await verifyCtx.WaitingList
            .SingleOrDefaultAsync(
                w => w.MatchId == matchId && w.PlayerId == confirmingPlayerId,
                TestContext.Current.CancellationToken);

        waitingEntry.Should().NotBeNull("player must be added to waiting list when all slots are full");
        waitingEntry!.Position.Should().BeGreaterThan(0);
    }

    // ─── AC-P4: DropIn gets 200 after window closes with released slots ──────

    /// <summary>
    /// Covers: AC-P4 — After windowClosesAt, a DropIn player who confirms when all original
    /// DropInSlots are taken but at least one Regular slot was released receives HTTP 200
    /// (successfully confirmed) rather than HTTP 409.
    ///
    /// Setup:
    ///   - Match has 2 Regular slots and 2 DropIn slots (maxPlayers = 4).
    ///   - Window is now Closed with ReleasedDropInSlots = 1 (one Regular slot freed).
    ///   - Effective DropIn limit = 2 (original) + 1 (released) = 3.
    ///   - 2 DropIns are already confirmed, leaving 1 slot available.
    ///   - Confirming DropIn player should succeed (HTTP 200).
    /// </summary>
    [Fact]
    public async Task PUT_me_DropIn_returns_200_when_released_regular_slots_create_effective_capacity()
    {
        await ResetAsync();

        var matchId = await CreateMatchAsync(OrganizerUserId, maxPlayers: 4, regularSlots: 2);

        // Force match to Closed with ReleasedDropInSlots = 1.
        // Also set windowClosesAt in the past so the DropIn window condition passes.
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
            await ctx.Database.ExecuteSqlAsync(
                $"UPDATE matches SET status = 'Closed', released_drop_in_slots = 1::smallint, window_closes_at = {DateTimeOffset.UtcNow.AddMinutes(-5)} WHERE id = {matchId}",
                TestContext.Current.CancellationToken);
        }

        // Add two DropIn players already confirmed (fills original 2 DropIn slots).
        var confirmedDropIn1 = Guid.NewGuid();
        var confirmedDropIn2 = Guid.NewGuid();
        await AddPresenceDirectlyAsync(matchId, confirmedDropIn1, PlayerType.DropIn, PresenceStatus.Confirmed);
        await AddPresenceDirectlyAsync(matchId, confirmedDropIn2, PlayerType.DropIn, PresenceStatus.Confirmed);

        // The player who will now confirm — has a pending presence row.
        var incomingDropIn = Guid.NewGuid();
        await AddPresenceDirectlyAsync(matchId, incomingDropIn, PlayerType.DropIn, PresenceStatus.Pending);

        var client = _fx.CreateAuthenticatedClient(incomingDropIn);
        var response = await client.PutAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences/me",
            new { status = "Confirmed" },
            TestContext.Current.CancellationToken);

        // Must be 200 OK — NOT 409 — because released slot made room.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "DropIn player should be confirmed using the released Regular slot capacity");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("status").GetString().Should().Be("Confirmed");
        body.GetProperty("confirmedAt").GetString().Should().NotBeNullOrEmpty();

        // Verify the presence is persisted as Confirmed.
        using var verifyScope = _fx.Factory.Services.CreateScope();
        var verifyCtx = verifyScope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var presence = await verifyCtx.Presences
            .SingleOrDefaultAsync(
                p => p.MatchId == matchId && p.PlayerId == incomingDropIn,
                TestContext.Current.CancellationToken);

        presence.Should().NotBeNull();
        presence!.Status.Should().Be(PresenceStatus.Confirmed);
    }

    // ─── POST /presences validation ──────────────────────────────────────────

    /// <summary>
    /// Covers: POST /api/v1/matches/{id}/presences returns 400 when PlayerType is missing.
    /// </summary>
    [Fact]
    public async Task POST_presences_without_PlayerType_returns_400()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences",
            new { playerId = Guid.NewGuid(), playerType = "" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Covers: POST /api/v1/matches/{id}/presences returns 400 when PlayerId is empty.
    /// </summary>
    [Fact]
    public async Task POST_presences_with_empty_PlayerId_returns_400()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences",
            new { playerId = Guid.Empty, playerType = "Regular" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Covers: POST /api/v1/matches/{id}/presences returns 403 when caller is not the organizer.
    /// </summary>
    [Fact]
    public async Task POST_presences_by_non_organizer_returns_403()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        var client = _fx.CreateAuthenticatedClient(OtherUserId);

        var response = await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences",
            new { playerId = Guid.NewGuid(), playerType = "Regular" },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Covers: POST /api/v1/matches/{id}/presences returns 409 when player already has a presence.
    /// </summary>
    [Fact]
    public async Task POST_presences_for_duplicate_player_returns_409()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);

        // Force match to Open (not past windowClosesAt) so the window check passes.
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
            await ctx.Database.ExecuteSqlAsync(
                $"UPDATE matches SET window_closes_at = {DateTimeOffset.UtcNow.AddDays(30)} WHERE id = {matchId}",
                TestContext.Current.CancellationToken);
        }

        var playerId = Guid.NewGuid();
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var payload = new { playerId, playerType = "Regular" };

        // First insertion — should succeed.
        var first = await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences", payload, TestContext.Current.CancellationToken);
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // Second insertion of the same player — should conflict.
        var second = await client.PostAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences", payload, TestContext.Current.CancellationToken);
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── DELETE /presences/{playerId} ────────────────────────────────────────

    /// <summary>
    /// Covers: DELETE /api/v1/matches/{id}/presences/{playerId} returns 204 when organizer removes
    /// a player from a Draft match.
    /// </summary>
    [Fact]
    public async Task DELETE_presences_by_organizer_on_Draft_match_returns_204()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);

        var playerId = Guid.NewGuid();
        await AddPresenceDirectlyAsync(matchId, playerId, PlayerType.Regular);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.DeleteAsync(
            $"/api/v1/matches/{matchId}/presences/{playerId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify the presence was deleted.
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var exists = await ctx.Presences.AnyAsync(
            p => p.MatchId == matchId && p.PlayerId == playerId,
            TestContext.Current.CancellationToken);
        exists.Should().BeFalse("presence should have been deleted");
    }

    /// <summary>
    /// Covers: DELETE returns 409 when match is not in Draft status.
    /// </summary>
    [Fact]
    public async Task DELETE_presences_on_non_Draft_match_returns_409()
    {
        await ResetAsync();
        var matchId = await CreateMatchAsync(OrganizerUserId);
        await ForceMatchStatusAsync(matchId, MatchStatus.Open);

        var playerId = Guid.NewGuid();
        await AddPresenceDirectlyAsync(matchId, playerId, PlayerType.Regular);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.DeleteAsync(
            $"/api/v1/matches/{matchId}/presences/{playerId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static MatchRequestPayload BuildMatchPayload(int maxPlayers = 4, int regularSlots = 2) =>
        new(
            Name: "Test Match",
            Description: null,
            Address: "Rua Teste, 123, São Paulo",
            Latitude: -23.5505,
            Longitude: -46.6333,
            DateTime: DateTimeOffset.UtcNow.AddDays(30),
            MaxPlayers: maxPlayers,
            RegularSlots: regularSlots,
            Price: null,
            Type: "OneOff",
            Frequency: null,
            DayOfWeek: null,
            WindowOpensAt: DateTimeOffset.UtcNow.AddDays(5),
            WindowClosesAt: DateTimeOffset.UtcNow.AddDays(15));

    private sealed record MatchRequestPayload(
        string Name,
        string? Description,
        string Address,
        double Latitude,
        double Longitude,
        DateTimeOffset DateTime,
        int MaxPlayers,
        int RegularSlots,
        decimal? Price,
        string Type,
        string? Frequency,
        int? DayOfWeek,
        DateTimeOffset WindowOpensAt,
        DateTimeOffset WindowClosesAt);
}
