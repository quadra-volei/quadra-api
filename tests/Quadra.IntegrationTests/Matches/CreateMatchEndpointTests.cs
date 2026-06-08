using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;

namespace Quadra.IntegrationTests.Matches;

/// <summary>
/// HTTP integration tests for POST /api/v1/matches.
/// Uses a real Postgres (postgis/postgis:16-3.4) via Testcontainers.
///
/// Covers: AC-1, AC-2, AC-3, AC-4, AC-5, AC-6, AC-7, AC-8.
/// </summary>
public sealed class CreateMatchEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    private readonly MatchesWebApplicationFactory _fx;
    private static readonly Guid OrganizerUserId = Guid.NewGuid();

    public CreateMatchEndpointTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private async Task ResetMatchesAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE matches RESTART IDENTITY CASCADE;");
    }

    private void ResetPublisher() => _fx.Publisher.ClearReceivedCalls();

    // ─── AC-1: valid OneOff → 201 + MatchResponse + Location header ─────────

    /// <summary>
    /// Covers: AC-1 — POST with valid OneOff payload returns 201 Created with MatchResponse body,
    /// Location header, and status = Draft.
    /// </summary>
    [Fact]
    public async Task Post_valid_OneOff_returns_201_with_MatchResponse_and_Location_header_and_Draft_status()
    {
        await ResetMatchesAsync();
        ResetPublisher();

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var request = BuildValidOneOff();

        var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("id").GetGuid().Should().NotBeEmpty();
        body.GetProperty("organizerId").GetGuid().Should().Be(OrganizerUserId);
        body.GetProperty("name").GetString().Should().Be("Sunday Volleyball");
        body.GetProperty("status").GetString().Should().Be("Draft");
        body.GetProperty("type").GetString().Should().Be("OneOff");
        body.GetProperty("dropInSlots").GetInt32().Should().Be(2); // maxPlayers(12) - regularSlots(10)

        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().Contain("/api/v1/matches/");
    }

    // ─── AC-2: valid Recurring → 201 ────────────────────────────────────────

    /// <summary>
    /// Covers: AC-2 — POST with valid Recurring payload (including Frequency and DayOfWeek) returns 201.
    /// </summary>
    [Fact]
    public async Task Post_valid_Recurring_returns_201_with_correct_type_and_frequency()
    {
        await ResetMatchesAsync();
        ResetPublisher();

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var request = BuildValidRecurring();

        var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("type").GetString().Should().Be("Recurring");
        body.GetProperty("frequency").GetString().Should().Be("Weekly");
        body.GetProperty("dayOfWeek").GetInt32().Should().Be(3);
        body.GetProperty("status").GetString().Should().Be("Draft");
    }

    // ─── AC-3: no auth → 401 ────────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-3 — POST without auth returns 401.
    /// </summary>
    [Fact]
    public async Task Post_without_auth_returns_401()
    {
        var client = _fx.CreateAnonymousClient();
        var request = BuildValidOneOff();

        var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── AC-4: MaxPlayers = 1 → 400 ─────────────────────────────────────────

    /// <summary>
    /// Covers: AC-4 — POST with MaxPlayers = 1 returns 400 with validation errors.
    /// </summary>
    [Fact]
    public async Task Post_with_MaxPlayers_equal_1_returns_400()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var request = BuildValidOneOff() with { MaxPlayers = 1, RegularSlots = 0 };

        var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── AC-5: RegularSlots > MaxPlayers → 400 ──────────────────────────────

    /// <summary>
    /// Covers: AC-5 — POST with RegularSlots > MaxPlayers returns 400.
    /// </summary>
    [Fact]
    public async Task Post_with_RegularSlots_exceeding_MaxPlayers_returns_400()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var request = BuildValidOneOff() with { MaxPlayers = 10, RegularSlots = 11 };

        var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── AC-6: Recurring without Frequency → 400 ────────────────────────────

    /// <summary>
    /// Covers: AC-6 — POST with Type=Recurring but missing Frequency returns 400.
    /// </summary>
    [Fact]
    public async Task Post_Recurring_without_Frequency_returns_400()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var request = BuildValidRecurring() with { Frequency = null };

        var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── AC-7: WindowClosesAt after DateTime → 400 ──────────────────────────

    /// <summary>
    /// Covers: AC-7 — POST with WindowClosesAt after DateTime returns 400.
    /// </summary>
    [Fact]
    public async Task Post_with_WindowClosesAt_after_DateTime_returns_400()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var matchDate = DateTimeOffset.UtcNow.AddDays(5);
        var request = BuildValidOneOff() with
        {
            DateTime = matchDate,
            WindowOpensAt = DateTimeOffset.UtcNow.AddDays(1),
            // WindowClosesAt is after DateTime — violates the rule
            WindowClosesAt = matchDate.AddHours(1),
        };

        var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ─── AC-8: MatchCreated event published ──────────────────────────────────

    /// <summary>
    /// Covers: AC-8 — POST publishes a MatchCreated SQS event after commit (verified via mock IEventPublisher).
    /// </summary>
    [Fact]
    public async Task Post_valid_OneOff_publishes_MatchCreated_event()
    {
        await ResetMatchesAsync();
        ResetPublisher();

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var request = BuildValidOneOff();

        var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var matchId = body.GetProperty("id").GetGuid();

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<MatchCreated>(e =>
                e.MatchId == matchId
                && e.OrganizerId == OrganizerUserId
                && e.Name == "Sunday Volleyball"
                && e.Type == "OneOff"),
            Arg.Any<CancellationToken>());
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static MatchRequestPayload BuildValidOneOff() =>
        new(
            Name: "Sunday Volleyball",
            Description: null,
            Address: "Rua Teste, 123, São Paulo",
            Latitude: -23.5505,
            Longitude: -46.6333,
            DateTime: DateTimeOffset.UtcNow.AddDays(30),
            MaxPlayers: 12,
            RegularSlots: 10,
            Price: null,
            Type: "OneOff",
            Frequency: null,
            DayOfWeek: null,
            WindowOpensAt: DateTimeOffset.UtcNow.AddDays(5),
            WindowClosesAt: DateTimeOffset.UtcNow.AddDays(15));

    private static MatchRequestPayload BuildValidRecurring() =>
        new(
            Name: "Weekly Volleyball",
            Description: null,
            Address: "Rua Teste, 123, São Paulo",
            Latitude: -23.5505,
            Longitude: -46.6333,
            DateTime: DateTimeOffset.UtcNow.AddDays(30),
            MaxPlayers: 12,
            RegularSlots: 10,
            Price: null,
            Type: "Recurring",
            Frequency: "Weekly",
            DayOfWeek: 3,
            WindowOpensAt: DateTimeOffset.UtcNow.AddDays(5),
            WindowClosesAt: DateTimeOffset.UtcNow.AddDays(15));

    // Internal record used only in tests so we don't take a direct dependency on the module's contract record.
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
