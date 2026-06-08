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
/// HTTP integration tests for DELETE /api/v1/matches/{id}.
///
/// Covers: AC-12, AC-13, AC-14.
/// (AC-15 is covered in unit tests via mocked IEventPublisher in CancelMatchHandlerTests;
/// here we additionally verify the event is received by the substitute publisher.)
/// </summary>
public sealed class CancelMatchEndpointTests : IClassFixture<MatchesWebApplicationFactory>
{
    private readonly MatchesWebApplicationFactory _fx;
    private static readonly Guid OrganizerUserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    public CancelMatchEndpointTests(MatchesWebApplicationFactory fx)
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

    private async Task<Guid> CreateMatchViaApiAsync(Guid organizerId)
    {
        var client = _fx.CreateAuthenticatedClient(organizerId);
        var request = BuildValidOneOff();
        var response = await client.PostAsJsonAsync("/api/v1/matches", request, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        return body.GetProperty("id").GetGuid();
    }

    private async Task ForceMatchStatusAsync(Guid matchId, MatchStatus status)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        // Update status directly in DB to set up terminal states for testing.
        // Parameters passed as FormattableString are safe from SQL injection.
        await ctx.Database.ExecuteSqlAsync(
            $"UPDATE matches SET status = {status.ToString()} WHERE id = {matchId}");
    }

    // ─── AC-12: organizer cancels Draft → 204 + Cancelled ────────────────────

    /// <summary>
    /// Covers: AC-12 — DELETE by organizer on a Draft match returns 204 and sets status = Cancelled.
    /// </summary>
    [Fact]
    public async Task Delete_Draft_match_by_organizer_returns_204_and_persists_Cancelled_status()
    {
        await ResetMatchesAsync();
        ResetPublisher();
        var matchId = await CreateMatchViaApiAsync(OrganizerUserId);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.DeleteAsync($"/api/v1/matches/{matchId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify persistence: status in DB is now Cancelled.
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var match = await ctx.Matches.SingleAsync(m => m.Id == matchId, TestContext.Current.CancellationToken);
        match.Status.Should().Be(MatchStatus.Cancelled);
    }

    // ─── AC-13: different user → 403 ────────────────────────────────────────

    /// <summary>
    /// Covers: AC-13 — DELETE by a different user returns 403.
    /// </summary>
    [Fact]
    public async Task Delete_match_by_non_organizer_returns_403()
    {
        await ResetMatchesAsync();
        ResetPublisher();
        var matchId = await CreateMatchViaApiAsync(OrganizerUserId);

        var client = _fx.CreateAuthenticatedClient(OtherUserId);
        var response = await client.DeleteAsync($"/api/v1/matches/{matchId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // Verify status unchanged in DB.
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var match = await ctx.Matches.SingleAsync(m => m.Id == matchId, TestContext.Current.CancellationToken);
        match.Status.Should().Be(MatchStatus.Draft);
    }

    // ─── AC-14: InProgress → 409 ────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-14 — DELETE when status = InProgress returns 409.
    /// </summary>
    [Fact]
    public async Task Delete_InProgress_match_returns_409()
    {
        await ResetMatchesAsync();
        ResetPublisher();
        var matchId = await CreateMatchViaApiAsync(OrganizerUserId);
        await ForceMatchStatusAsync(matchId, MatchStatus.InProgress);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.DeleteAsync($"/api/v1/matches/{matchId}", TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    // ─── AC-15 (integration perspective): MatchStatusChanged event published ─

    /// <summary>
    /// Covers: AC-15 — DELETE publishes MatchStatusChanged event after successful cancellation.
    /// Integration-level verification via the substitute IEventPublisher.
    /// </summary>
    [Fact]
    public async Task Delete_Draft_match_publishes_MatchStatusChanged_event()
    {
        await ResetMatchesAsync();
        ResetPublisher();
        var matchId = await CreateMatchViaApiAsync(OrganizerUserId);

        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.DeleteAsync($"/api/v1/matches/{matchId}", TestContext.Current.CancellationToken);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await _fx.Publisher.Received(1).PublishAsync(
            Arg.Is<MatchStatusChanged>(e =>
                e.MatchId == matchId
                && e.OrganizerId == OrganizerUserId
                && e.PreviousStatus == "Draft"
                && e.NewStatus == "Cancelled"),
            Arg.Any<CancellationToken>());
    }

    // ─── 404 ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Delete_non_existent_match_returns_404()
    {
        var nonExistentId = Guid.NewGuid();
        var client = _fx.CreateAuthenticatedClient(OrganizerUserId);
        var response = await client.DeleteAsync($"/api/v1/matches/{nonExistentId}", TestContext.Current.CancellationToken);
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
