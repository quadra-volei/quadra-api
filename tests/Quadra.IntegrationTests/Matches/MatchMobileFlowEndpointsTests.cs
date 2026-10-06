using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Matches.Persistence;
using Quadra.Modules.Profile.Abstractions;

namespace Quadra.IntegrationTests.Matches;

/// <summary>
/// HTTP integration tests for the match flow the mobile app uses (frontend S11 create, S12
/// detail, S5 home), against a real Postgres:
///
///  - creating a match from the mobile form: format, level, duration, visibility, monthly price,
///    multi-day recurrence and a window given as "opens N hours before";
///  - the window opening by the clock alone (no worker, no manual endpoint);
///  - any player joining an open match by confirming; private matches by invite code;
///  - guests without an account filling slots, counted against the capacity;
///  - the detail payload (organizer, roster with identities, guests, caller standing);
///  - the caller's upcoming matches.
/// </summary>
public sealed class MatchMobileFlowEndpointsTests : IClassFixture<MatchesWebApplicationFactory>
{
    private readonly MatchesWebApplicationFactory _fx;

    public MatchMobileFlowEndpointsTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private async Task ResetAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await ctx.Database.ExecuteSqlRawAsync(
            "TRUNCATE TABLE match_guests, waiting_list, match_presences, matches RESTART IDENTITY CASCADE;", Ct);
    }

    /// <summary>The body the mobile create form sends. Starts in 3h with a 24h window → already open.</summary>
    private static Dictionary<string, object?> MobilePayload(int maxPlayers = 4) => new()
    {
        ["name"] = "Vôlei de Quinta",
        ["description"] = null,
        ["address"] = "Arena Central, São Paulo",
        ["latitude"] = -23.5505,
        ["longitude"] = -46.6333,
        ["dateTime"] = DateTimeOffset.UtcNow.AddHours(3),
        ["maxPlayers"] = maxPlayers,
        ["regularSlots"] = maxPlayers,
        ["price"] = 15m,
        ["type"] = "OneOff",
        ["frequency"] = null,
        ["dayOfWeek"] = null,
        ["format"] = "4X4",
        ["level"] = "Intermediate",
        ["durationMinutes"] = 90,
        ["visibility"] = "Open",
        ["confirmationOpensHoursBefore"] = 24,
    };

    private async Task<JsonElement> CreateAsync(Guid organizerId, Dictionary<string, object?> payload)
    {
        var response = await _fx.CreateAuthenticatedClient(organizerId)
            .PostAsJsonAsync("/api/v1/matches", payload, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    private Task<HttpResponseMessage> ConfirmAsync(Guid matchId, Guid playerId, string? inviteCode = null) =>
        _fx.CreateAuthenticatedClient(playerId).PutAsJsonAsync(
            $"/api/v1/matches/{matchId}/presences/me", new { status = "Confirmed", inviteCode }, Ct);

    private async Task<JsonElement> DetailAsync(Guid matchId, Guid callerId)
    {
        var response = await _fx.CreateAuthenticatedClient(callerId)
            .GetAsync($"/api/v1/matches/{matchId}/detail", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        return await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
    }

    // ═══ create ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Covers: F1.1 (R1, mobile S11) — the create form's fields are stored and returned, the
    /// window is derived from "opens N hours before", and a window that is already due makes the
    /// match Open at once.
    /// </summary>
    [Fact]
    public async Task Create_from_the_mobile_form_stores_its_fields_and_opens_a_due_window()
    {
        await ResetAsync();

        var match = await CreateAsync(Guid.NewGuid(), MobilePayload());

        match.GetProperty("format").GetString().Should().Be("4X4");
        match.GetProperty("level").GetString().Should().Be("Intermediate");
        match.GetProperty("durationMinutes").GetInt32().Should().Be(90);
        match.GetProperty("visibility").GetString().Should().Be("Open");
        match.GetProperty("status").GetString().Should().Be("Open");
        match.GetProperty("windowClosesAt").GetDateTimeOffset()
            .Should().Be(match.GetProperty("dateTime").GetDateTimeOffset());
        match.GetProperty("windowOpensAt").GetDateTimeOffset()
            .Should().Be(match.GetProperty("dateTime").GetDateTimeOffset().AddHours(-24));
        match.TryGetProperty("inviteCode", out _).Should().BeFalse("the code is only in the organizer's detail");
    }

    /// <summary>
    /// Covers: F1.1 — a recurring match with several week days and a monthly price; a window that
    /// only opens later leaves the match in Draft.
    /// </summary>
    [Fact]
    public async Task Create_recurring_with_several_days_and_a_monthly_price()
    {
        await ResetAsync();
        var payload = MobilePayload();
        payload["dateTime"] = DateTimeOffset.UtcNow.AddDays(10);
        payload["type"] = "Recurring";
        payload["frequency"] = "Weekly";
        payload["recurrenceDays"] = new[] { 4, 2 };
        payload["priceMonthly"] = 80m;
        payload["confirmationOpensHoursBefore"] = 48;

        var match = await CreateAsync(Guid.NewGuid(), payload);

        match.GetProperty("status").GetString().Should().Be("Draft");
        match.GetProperty("recurrenceDays").EnumerateArray().Select(d => d.GetInt32()).Should().Equal(2, 4);
        match.GetProperty("dayOfWeek").GetInt32().Should().Be(2);
        match.GetProperty("priceMonthly").GetDecimal().Should().Be(80m);
    }

    /// <summary>Covers: F1.1 validation of the new fields.</summary>
    [Theory]
    [InlineData("format", "5X5")]
    [InlineData("level", "Elite")]
    [InlineData("visibility", "Secret")]
    [InlineData("durationMinutes", 5)]
    [InlineData("confirmationOpensHoursBefore", 0)]
    public async Task Create_with_an_invalid_mobile_field_returns_400(string field, object value)
    {
        await ResetAsync();
        var payload = MobilePayload();
        payload[field] = value;

        var response = await _fx.CreateAuthenticatedClient(Guid.NewGuid())
            .PostAsJsonAsync("/api/v1/matches", payload, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers: a private match must say how players are invited.</summary>
    [Fact]
    public async Task Create_private_without_invite_mode_returns_400()
    {
        await ResetAsync();
        var payload = MobilePayload();
        payload["visibility"] = "Private";

        var response = await _fx.CreateAuthenticatedClient(Guid.NewGuid())
            .PostAsJsonAsync("/api/v1/matches", payload, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ═══ joining + detail ═════════════════════════════════════════════════════

    /// <summary>
    /// Covers: F1.2 (mobile S12) — the organizer and another player join an open match by
    /// confirming; the detail shows the organizer's identity, the roster, the counts and each
    /// caller's own standing.
    /// </summary>
    [Fact]
    public async Task Players_join_an_open_match_and_the_detail_reflects_it()
    {
        await ResetAsync();
        var organizer = Guid.NewGuid();
        var player = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        await OnboardAsync(organizer, "Ana", "Souza", "ana_org");
        var matchId = (await CreateAsync(organizer, MobilePayload())).GetProperty("id").GetGuid();

        var before = await DetailAsync(matchId, player);
        before.GetProperty("canJoin").GetBoolean().Should().BeTrue();
        before.GetProperty("myPresence").ValueKind.Should().Be(JsonValueKind.Null);
        before.GetProperty("confirmedCount").GetInt32().Should().Be(0);

        (await ConfirmAsync(matchId, organizer)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ConfirmAsync(matchId, player)).StatusCode.Should().Be(HttpStatusCode.OK);

        var detail = await DetailAsync(matchId, player);
        detail.GetProperty("organizer").GetProperty("displayName").GetString().Should().Be("Ana Souza");
        detail.GetProperty("organizer").GetProperty("handle").GetString().Should().Be("ana_org");
        detail.GetProperty("organizer").GetProperty("position").GetString().Should().Be("LEV");
        detail.GetProperty("confirmedCount").GetInt32().Should().Be(2);
        detail.GetProperty("openSlots").GetInt32().Should().Be(2);
        detail.GetProperty("canJoin").GetBoolean().Should().BeFalse("the caller is already on the list");
        detail.GetProperty("myPresence").GetProperty("status").GetString().Should().Be("Confirmed");
        detail.GetProperty("myPresence").GetProperty("playerType").GetString().Should().Be("Regular");
        detail.GetProperty("inviteCode").ValueKind.Should().Be(JsonValueKind.Null);

        var roster = detail.GetProperty("players").EnumerateArray().ToList();
        roster.Select(p => p.GetProperty("player").GetProperty("userId").GetGuid())
            .Should().BeEquivalentTo([organizer, player]);
        roster.Should().OnlyContain(p => p.GetProperty("status").GetString() == "Confirmed");
        // A player without a profile still renders, with the placeholder name.
        roster.Single(p => p.GetProperty("player").GetProperty("userId").GetGuid() == player)
            .GetProperty("player").GetProperty("displayName").GetString().Should().Be("Player");

        (await DetailAsync(matchId, stranger)).GetProperty("myPresence").ValueKind.Should().Be(JsonValueKind.Null);
    }

    /// <summary>
    /// Covers: F1.2 — once the slots are taken the next player is put on the waiting list (409
    /// with the position) and sees that position in the detail.
    /// </summary>
    [Fact]
    public async Task Joining_a_full_match_puts_the_player_on_the_waiting_list()
    {
        await ResetAsync();
        var organizer = Guid.NewGuid();
        var late = Guid.NewGuid();
        var matchId = (await CreateAsync(organizer, MobilePayload(maxPlayers: 2))).GetProperty("id").GetGuid();
        (await ConfirmAsync(matchId, organizer)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await ConfirmAsync(matchId, Guid.NewGuid())).StatusCode.Should().Be(HttpStatusCode.OK);

        var response = await ConfirmAsync(matchId, late);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("waitingListPosition").GetInt32().Should().Be(1);
        var detail = await DetailAsync(matchId, late);
        detail.GetProperty("myWaitingListPosition").GetInt32().Should().Be(1);
        detail.GetProperty("openSlots").GetInt32().Should().Be(0);
        detail.GetProperty("waitingList").GetArrayLength().Should().Be(1);
    }

    /// <summary>
    /// Covers: private match by invite code — only the organizer sees the code; joining needs it.
    /// </summary>
    [Fact]
    public async Task Private_match_is_joined_with_the_invite_code_only()
    {
        await ResetAsync();
        var organizer = Guid.NewGuid();
        var invited = Guid.NewGuid();
        var payload = MobilePayload();
        payload["visibility"] = "Private";
        payload["inviteMode"] = "Code";
        var matchId = (await CreateAsync(organizer, payload)).GetProperty("id").GetGuid();

        var code = (await DetailAsync(matchId, organizer)).GetProperty("inviteCode").GetString();
        code.Should().HaveLength(8);
        var seenByInvited = await DetailAsync(matchId, invited);
        seenByInvited.GetProperty("inviteCode").ValueKind.Should().Be(JsonValueKind.Null);
        seenByInvited.GetProperty("canJoin").GetBoolean().Should().BeFalse();

        (await ConfirmAsync(matchId, invited)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ConfirmAsync(matchId, invited, "WRONG123")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await ConfirmAsync(matchId, invited, code)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ═══ guests ═══════════════════════════════════════════════════════════════

    /// <summary>
    /// Covers: guests without an account (mobile S12) — only the organizer adds them; they count
    /// as confirmed, fill the capacity (409 when full) and can be removed.
    /// </summary>
    [Fact]
    public async Task Organizer_fills_slots_with_guests_up_to_the_capacity()
    {
        await ResetAsync();
        var organizer = Guid.NewGuid();
        var other = Guid.NewGuid();
        var matchId = (await CreateAsync(organizer, MobilePayload(maxPlayers: 2))).GetProperty("id").GetGuid();
        var organizerClient = _fx.CreateAuthenticatedClient(organizer);
        var url = $"/api/v1/matches/{matchId}/guests";

        (await _fx.CreateAuthenticatedClient(other).PostAsJsonAsync(url, new { name = "Intruso" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await organizerClient.PostAsJsonAsync(url, new { name = "  " }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await organizerClient.PostAsJsonAsync(url, new { name = "Zé", position = "GOL" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var first = await organizerClient.PostAsJsonAsync(url, new { name = " Zé da Praia ", position = "LIB" }, Ct);
        first.StatusCode.Should().Be(HttpStatusCode.Created);
        var guest = await first.Content.ReadFromJsonAsync<JsonElement>(Ct);
        guest.GetProperty("name").GetString().Should().Be("Zé da Praia");
        guest.GetProperty("position").GetString().Should().Be("LIB");
        (await organizerClient.PostAsJsonAsync(url, new { name = "Maria" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await organizerClient.PostAsJsonAsync(url, new { name = "Sem vaga" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Conflict);

        var full = await DetailAsync(matchId, other);
        full.GetProperty("guests").GetArrayLength().Should().Be(2);
        full.GetProperty("confirmedCount").GetInt32().Should().Be(2);
        full.GetProperty("openSlots").GetInt32().Should().Be(0);
        // No slot left for an account either: the player waits.
        (await ConfirmAsync(matchId, other)).StatusCode.Should().Be(HttpStatusCode.Conflict);

        (await organizerClient.DeleteAsync($"{url}/{guest.GetProperty("id").GetGuid()}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await organizerClient.DeleteAsync($"{url}/{Guid.NewGuid()}", Ct))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DetailAsync(matchId, other)).GetProperty("openSlots").GetInt32().Should().Be(1);
    }

    // ═══ mine ═════════════════════════════════════════════════════════════════

    /// <summary>
    /// Covers: mobile S5 "Próximas partidas" — the caller's upcoming matches are the ones they
    /// organize or joined, soonest first, with counts; other people's matches are not listed.
    /// </summary>
    [Fact]
    public async Task Mine_lists_the_matches_the_caller_organizes_or_joined()
    {
        await ResetAsync();
        var me = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();

        var later = MobilePayload();
        later["name"] = "Minha, depois";
        later["dateTime"] = DateTimeOffset.UtcNow.AddDays(5);
        await CreateAsync(me, later);

        var joinedId = (await CreateAsync(someoneElse, MobilePayload())).GetProperty("id").GetGuid();
        (await ConfirmAsync(joinedId, me)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await _fx.CreateAuthenticatedClient(someoneElse).PostAsJsonAsync(
            $"/api/v1/matches/{joinedId}/guests", new { name = "Convidado" }, Ct))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var notMine = MobilePayload();
        notMine["name"] = "De outra pessoa";
        await CreateAsync(someoneElse, notMine);

        var response = await _fx.CreateAuthenticatedClient(me).GetAsync("/api/v1/matches/mine", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct))
            .GetProperty("items").EnumerateArray().ToList();

        items.Select(i => i.GetProperty("match").GetProperty("name").GetString())
            .Should().Equal("Vôlei de Quinta", "Minha, depois");
        items[0].GetProperty("isOrganizer").GetBoolean().Should().BeFalse();
        items[0].GetProperty("myStatus").GetString().Should().Be("Confirmed");
        items[0].GetProperty("confirmedCount").GetInt32().Should().Be(2, "me plus one guest");
        items[0].GetProperty("openSlots").GetInt32().Should().Be(2);
        items[1].GetProperty("isOrganizer").GetBoolean().Should().BeTrue();
        items[1].GetProperty("myStatus").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    /// <summary>Provisions a profile and completes onboarding, so the player has a real identity.</summary>
    private async Task OnboardAsync(Guid userId, string firstName, string lastName, string handle)
    {
        using (var scope = _fx.Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPlayerProfileProvisioner>()
                .EnsureProfileAsync(userId, Ct);
        }

        var response = await _fx.CreateAuthenticatedClient(userId).PutAsJsonAsync(
            "/api/v1/profiles/me",
            new
            {
                firstName,
                lastName,
                handle,
                birthDate = "1995-05-20",
                position = "LEV",
                modality = "Indoor",
                level = "Intermediate",
            },
            Ct);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
