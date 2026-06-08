using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.IntegrationTests.Matches;

/// <summary>
/// HTTP integration tests for GET /api/v1/matches (paginated list).
///
/// Covers: AC-11.
/// </summary>
public sealed class ListMatchesEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    private readonly MatchesWebApplicationFactory _fx;
    private static readonly Guid OrganizerUserId = Guid.NewGuid();

    public ListMatchesEndpointTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private async Task ResetMatchesAsync()
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        await ctx.Database.ExecuteSqlRawAsync("TRUNCATE TABLE matches RESTART IDENTITY CASCADE;");
    }

    private async Task SeedMatchesAsync(int count)
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        for (var i = 0; i < count; i++)
        {
            var request = BuildValidOneOff($"Match {i + 1}");
            var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);
            response.EnsureSuccessStatusCode();
        }
    }

    // ─── AC-11: paginated results with TotalCount ─────────────────────────────

    /// <summary>
    /// Covers: AC-11 — GET /api/v1/matches?page=1&amp;pageSize=10 returns paginated results with TotalCount.
    /// </summary>
    [Fact]
    public async Task Get_matches_with_pagination_returns_paginated_results_with_TotalCount()
    {
        await ResetMatchesAsync();
        await SeedMatchesAsync(15);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync("/api/v1/matches?page=1&pageSize=10", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("page").GetInt32().Should().Be(1);
        body.GetProperty("pageSize").GetInt32().Should().Be(10);
        body.GetProperty("totalCount").GetInt32().Should().Be(15);
        body.GetProperty("items").GetArrayLength().Should().Be(10);
    }

    [Fact]
    public async Task Get_matches_second_page_returns_remaining_items()
    {
        await ResetMatchesAsync();
        await SeedMatchesAsync(15);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync("/api/v1/matches?page=2&pageSize=10", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("page").GetInt32().Should().Be(2);
        body.GetProperty("totalCount").GetInt32().Should().Be(15);
        body.GetProperty("items").GetArrayLength().Should().Be(5);
    }

    [Fact]
    public async Task Get_matches_with_invalid_page_returns_400()
    {
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.GetAsync("/api/v1/matches?page=0&pageSize=10", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Get_matches_without_auth_returns_401()
    {
        var client = _fx.CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/matches?page=1&pageSize=10", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ─── Helper ──────────────────────────────────────────────────────────────

    private static MatchRequestPayload BuildValidOneOff(string name = "Volleyball Match") =>
        new(
            Name: name,
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
