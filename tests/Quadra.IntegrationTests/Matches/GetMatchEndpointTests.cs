using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.IntegrationTests.Matches;

/// <summary>
/// HTTP integration tests for GET /api/v1/matches/{id}.
///
/// Covers: AC-9, AC-10.
/// </summary>
public sealed class GetMatchEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    private readonly MatchesWebApplicationFactory _fx;
    private static readonly Guid OrganizerUserId = Guid.NewGuid();

    public GetMatchEndpointTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private async Task ResetMatchesAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE matches RESTART IDENTITY CASCADE;");
    }

    private async Task<Guid> CreateMatchViaApiAsync()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var request = BuildValidOneOff();
        var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    // ─── AC-9: GET existing match returns 200 ────────────────────────────────

    /// <summary>
    /// Covers: AC-9 — GET /api/v1/matches/{id} returns 200 with the correct MatchResponse for an existing match.
    /// </summary>
    [Fact]
    public async Task Get_existing_match_returns_200_with_correct_MatchResponse()
    {
        await ResetMatchesAsync();
        var matchId = await CreateMatchViaApiAsync();

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync($"/api/v1/matches/{matchId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("id").GetGuid().Should().Be(matchId);
        body.GetProperty("organizerId").GetGuid().Should().Be(OrganizerUserId);
        body.GetProperty("name").GetString().Should().Be("Sunday Volleyball");
        body.GetProperty("status").GetString().Should().Be("Draft");
        body.GetProperty("type").GetString().Should().Be("OneOff");
    }

    // ─── AC-10: GET non-existent match returns 404 ───────────────────────────

    /// <summary>
    /// Covers: AC-10 — GET /api/v1/matches/{id} returns 404 for a non-existent id.
    /// </summary>
    [Fact]
    public async Task Get_non_existent_match_returns_404()
    {
        var nonExistentId = Guid.NewGuid();
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);

        var response = await client.GetAsync($"/api/v1/matches/{nonExistentId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

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
