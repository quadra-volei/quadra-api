using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Profile.Abstractions;

namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>
/// HTTP integration tests for the F2.2 player-card endpoints (<c>GET /profiles/me/card</c> and
/// <c>GET /profiles/{userId}/card</c>), backed by a real Postgres. The card is driven through the
/// Profile-owned write interface (<see cref="IPlayerStatsWriter"/>) — exactly as the Background
/// Worker would on a <c>MatchSummaryGenerated</c> event — because no HTTP endpoint mutates it.
///
/// Coverage per acceptance criterion:
///   - "endpoint returns card DATA as JSON": 200 returns stats/level/positions/IsPremium, no image;
///   - "generated only after 3 recorded matches": &lt;3 → 409, at/after 3 → 200;
///   - "generated_at set once and preserved, refreshed_at updates each match": refresh comparison;
///   - "premium flag resolves via stub → always false": IsPremium == false;
///   - "404 when no profile, 409 when card not generated, 401 without JWT" for both endpoints;
///   - "idempotency: redelivery reproduces the identical card snapshot".
/// </summary>
[Collection("Profile")]
public sealed class PlayerCardEndpointsTests
{
    private readonly ProfileWebApplicationFactory _fx;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public PlayerCardEndpointsTests(ProfileWebApplicationFactory fx)
    {
        _fx = fx;
    }

    // ═══ GET /profiles/me/card ═════════════════════════════════════════════════

    /// <summary>Covers: F2.2 GET /profiles/me/card — 401 when no JWT is provided.</summary>
    [Fact]
    public async Task GET_me_card_without_JWT_returns_401()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/profiles/me/card", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Covers: F2.2 GET /profiles/me/card — 404 when the caller has no profile.</summary>
    [Fact]
    public async Task GET_me_card_when_no_profile_returns_404()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync("/api/v1/profiles/me/card", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers: F2.2 "generated only after 3 recorded matches; below 3 → no card row → GET returns 409".
    /// A provisioned player with only 2 recorded matches has no card yet.
    /// </summary>
    [Fact]
    public async Task GET_me_card_below_threshold_returns_409()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        await ApplyMatchAsync(userId, "Win", daysAgo: 5);
        await ApplyMatchAsync(userId, "Loss", daysAgo: 4);

        var client = _fx.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync("/api/v1/profiles/me/card", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Covers: F2.2 "at/after 3 → card exists"; the endpoint returns card DATA as JSON — stats, level,
    /// positions and the free/premium flag (never an image). Premium resolves to false via the stub.
    /// </summary>
    [Fact]
    public async Task GET_me_card_at_threshold_returns_200_with_card_data()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        await SetPositionsAsync(userId, "Setter", "Libero");
        await ApplyMatchAsync(userId, "Win", daysAgo: 6);
        await ApplyMatchAsync(userId, "Win", daysAgo: 5);
        await ApplyMatchAsync(userId, "Loss", daysAgo: 4);

        var client = _fx.CreateAuthenticatedClient(userId);
        var response = await client.GetAsync("/api/v1/profiles/me/card", Ct);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var card = await response.Content.ReadFromJsonAsync<CardDto>(Ct);
        card.Should().NotBeNull();
        card!.UserId.Should().Be(userId);
        card.DisplayName.Should().Be("Card Player");
        card.PrimaryPosition.Should().Be("Setter");
        card.SecondaryPosition.Should().Be("Libero");
        card.Level.Should().Be("Beginner");
        card.Stats.Should().Be(new StatsDto(MatchesPlayed: 3, Wins: 2, Losses: 1, Draws: 0, MvpsReceived: 0));
        card.IsPremium.Should().BeFalse("the MVP premium reader is the free-tier stub");
        card.GeneratedAt.Should().Be(card.RefreshedAt, "on first generation the two timestamps match");
    }

    /// <summary>
    /// Covers: F2.2 "generated_at is set once on first crossing and preserved across refreshes;
    /// refreshed_at updates each finished match". A 4th match refreshes the snapshot: generated_at is
    /// unchanged, refreshed_at advances past it and the stats are recomputed.
    /// </summary>
    [Fact]
    public async Task GET_me_card_preserves_generated_at_and_updates_refreshed_at_on_refresh()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        await ApplyMatchAsync(userId, "Win", daysAgo: 8);
        await ApplyMatchAsync(userId, "Win", daysAgo: 7);
        await ApplyMatchAsync(userId, "Win", daysAgo: 6);

        var client = _fx.CreateAuthenticatedClient(userId);
        var first = await client.GetFromJsonAsync<CardDto>("/api/v1/profiles/me/card", Ct);
        first!.Stats.MatchesPlayed.Should().Be(3);

        // A 4th finished match refreshes the snapshot.
        await ApplyMatchAsync(userId, "Loss", daysAgo: 5);
        var second = await client.GetFromJsonAsync<CardDto>("/api/v1/profiles/me/card", Ct);

        second!.GeneratedAt.Should().Be(first.GeneratedAt, "generated_at is set once and preserved");
        second.RefreshedAt.Should().BeAfter(second.GeneratedAt, "refreshed_at advances on each match");
        second.Stats.Should().Be(new StatsDto(4, 3, 1, 0, 0), "the snapshot is recomputed on refresh");
    }

    /// <summary>
    /// Covers: F2.2 idempotency — "a redelivered MatchSummaryGenerated reproduces the identical card
    /// snapshot (recompute-not-increment)". Redelivering an already-applied match leaves the card data
    /// and generated_at unchanged.
    /// </summary>
    [Fact]
    public async Task GET_me_card_is_idempotent_on_redelivery()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var first = await ApplyMatchAsync(userId, "Win", daysAgo: 6);
        await ApplyMatchAsync(userId, "Loss", daysAgo: 5);
        await ApplyMatchAsync(userId, "Draw", daysAgo: 4);

        var client = _fx.CreateAuthenticatedClient(userId);
        var before = await client.GetFromJsonAsync<CardDto>("/api/v1/profiles/me/card", Ct);

        // Redeliver the first match (same match id) — recompute-not-increment must not drift.
        await ApplyMatchAsync(userId, "Win", daysAgo: 6, matchId: first);
        var after = await client.GetFromJsonAsync<CardDto>("/api/v1/profiles/me/card", Ct);

        after!.Stats.Should().Be(before!.Stats, "redelivery must not double-count");
        after.GeneratedAt.Should().Be(before.GeneratedAt, "redelivery must not move generated_at");
        after.Level.Should().Be(before.Level);
        after.DisplayName.Should().Be(before.DisplayName);
    }

    // ═══ GET /profiles/{userId}/card ═══════════════════════════════════════════

    /// <summary>Covers: F2.2 GET /profiles/{userId}/card — 401 when no JWT is provided.</summary>
    [Fact]
    public async Task GET_card_by_userId_without_JWT_returns_401()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAnonymousClient();
        var response = await client.GetAsync($"/api/v1/profiles/{Guid.NewGuid()}/card", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Covers: F2.2 GET /profiles/{userId}/card — 404 for an unknown player.</summary>
    [Fact]
    public async Task GET_card_by_userId_when_unknown_returns_404()
    {
        await _fx.ResetAsync();
        var caller = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await caller.GetAsync($"/api/v1/profiles/{Guid.NewGuid()}/card", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers: F2.2 GET /profiles/{userId}/card — 409 when the target has a profile but fewer than 3
    /// recorded matches (no card row yet).
    /// </summary>
    [Fact]
    public async Task GET_card_by_userId_below_threshold_returns_409()
    {
        await _fx.ResetAsync();
        var target = Guid.NewGuid();
        await ProvisionAsync(target);
        await ApplyMatchAsync(target, "Win", daysAgo: 3);

        var caller = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await caller.GetAsync($"/api/v1/profiles/{target}/card", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    /// <summary>
    /// Covers: F2.2 GET /profiles/{userId}/card — 200 returns another player's card once generated,
    /// with the free-tier premium flag.
    /// </summary>
    [Fact]
    public async Task GET_card_by_userId_returns_other_players_card()
    {
        await _fx.ResetAsync();
        var target = Guid.NewGuid();
        await ProvisionAsync(target);
        await ApplyMatchAsync(target, "Win", daysAgo: 6);
        await ApplyMatchAsync(target, "Win", daysAgo: 5);
        await ApplyMatchAsync(target, "Win", daysAgo: 4);

        var caller = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var card = await caller.GetFromJsonAsync<CardDto>($"/api/v1/profiles/{target}/card", Ct);

        card.Should().NotBeNull();
        card!.UserId.Should().Be(target);
        card.Stats.MatchesPlayed.Should().Be(3);
        card.IsPremium.Should().BeFalse();
    }

    // ═══ Seeding helpers (drive the Background-Worker write path) ═══════════════

    private Task ProvisionAsync(Guid userId) =>
        _fx.WithScopeAsync(async sp =>
        {
            var provisioner = sp.GetRequiredService<IPlayerProfileProvisioner>();
            await provisioner.EnsureProfileAsync(userId, Ct);
        });

    private Task SetPositionsAsync(Guid userId, string primary, string? secondary) =>
        _fx.WithScopeAsync(async sp =>
        {
            var handler = sp.GetRequiredService<Quadra.Modules.Profile.Application.UpdateProfileHandler>();
            await handler.HandleAsync(
                userId,
                new Quadra.Modules.Profile.Contracts.UpdateProfileRequest("Card Player", primary, secondary, null),
                Ct);
        });

    private async Task<Guid> ApplyMatchAsync(
        Guid userId,
        string outcome,
        int daysAgo,
        bool wasMvp = false,
        Guid? matchId = null)
    {
        var id = matchId ?? Guid.NewGuid();
        await _fx.WithScopeAsync(async sp =>
        {
            var writer = sp.GetRequiredService<IPlayerStatsWriter>();
            var participation = new FinishedMatchParticipation(
                PlayerId: userId,
                MatchId: id,
                MatchName: "Match",
                MatchDateTime: DateTimeOffset.UtcNow.AddDays(-daysAgo),
                TeamId: Guid.NewGuid(),
                Outcome: outcome,
                WasMvp: wasMvp,
                DurationSeconds: 3600,
                FinishedAt: DateTimeOffset.UtcNow.AddDays(-daysAgo));
            await writer.ApplyFinishedMatchAsync(participation, Ct);
        });
        return id;
    }

    // ═══ Response DTOs (web-default camelCase, case-insensitive binding) ════════

    private sealed record CardDto(
        Guid UserId,
        string DisplayName,
        string? PrimaryPosition,
        string? SecondaryPosition,
        string Level,
        StatsDto Stats,
        bool IsPremium,
        DateTimeOffset GeneratedAt,
        DateTimeOffset RefreshedAt);

    private sealed record StatsDto(
        int MatchesPlayed,
        int Wins,
        int Losses,
        int Draws,
        int MvpsReceived);
}
