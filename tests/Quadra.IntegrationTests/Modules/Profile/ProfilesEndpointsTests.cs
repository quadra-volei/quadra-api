using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Quadra.Modules.Profile.Abstractions;

namespace Quadra.IntegrationTests.Modules.Profile;

/// <summary>
/// HTTP integration tests for the F2.1 player-profile endpoints, backed by a real Postgres.
/// Stats/level/history are driven through the Profile-owned write interfaces
/// (<see cref="IPlayerProfileProvisioner"/>, <see cref="IPlayerStatsWriter"/>) — exactly as the
/// Background Worker would — because no HTTP endpoint may mutate them (SCOPE "OUT: manual stat editing").
///
/// Coverage per acceptance criterion:
///   - "photo, name, primary/secondary position": GET/PUT round-trip + validation + photo URL resolve;
///   - "automatically calculated level": Beginner defaults; Intermediate after 10 matches + 1 MVP;
///   - "aggregated stats (played/wins/losses/draws/mvps)": recompute path + idempotency on redelivery;
///   - "match history with pagination": newest-first ordering, HasNextPage, page/pageSize bounds;
///   - "OUT: manual stat editing": PUT never changes stats/level; no stat/history mutation route.
/// </summary>
[Collection("Profile")]
public sealed class ProfilesEndpointsTests
{
    private readonly ProfileWebApplicationFactory _fx;
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ProfilesEndpointsTests(ProfileWebApplicationFactory fx)
    {
        _fx = fx;
    }

    // ═══ GET /profiles/me ══════════════════════════════════════════════════════

    /// <summary>Covers: F2.1 GET /profiles/me — 401 when no JWT is provided.</summary>
    [Fact]
    public async Task GET_me_without_JWT_returns_401()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAnonymousClient();
        var response = await client.GetAsync("/api/v1/profiles/me", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    /// <summary>Covers: F2.1 GET /profiles/me — 404 when the caller has no profile yet.</summary>
    [Fact]
    public async Task GET_me_when_no_profile_returns_404()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync("/api/v1/profiles/me", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers: F2.1 "provisioning" + default level/stats — after provisioning, GET returns the
    /// placeholder name, Beginner level, null positions/photo and all-zero stats.
    /// </summary>
    [Fact]
    public async Task GET_me_after_provision_returns_placeholder_defaults()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);

        var client = _fx.CreateAuthenticatedClient(userId);
        var profile = await client.GetFromJsonAsync<ProfileDto>("/api/v1/profiles/me", Ct);

        profile.Should().NotBeNull();
        profile!.UserId.Should().Be(userId);
        profile.DisplayName.Should().Be("Player");
        profile.PrimaryPosition.Should().BeNull();
        profile.SecondaryPosition.Should().BeNull();
        profile.PhotoUrl.Should().BeNull();
        profile.Level.Should().Be("Beginner");
        profile.Stats.Should().Be(new StatsDto(0, 0, 0, 0, 0));
    }

    // ═══ PUT /profiles/me ══════════════════════════════════════════════════════

    /// <summary>
    /// Covers: F2.1 "photo, name, primary/secondary position" — PUT updates the editable fields and
    /// GET reflects them; the photo reference resolves to a short-lived read URL.
    /// </summary>
    [Fact]
    public async Task PUT_me_updates_editable_fields_and_resolves_photo_url()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);

        var photoKey = $"profiles/{userId}/photo/{Guid.NewGuid()}.jpg";
        var request = new UpdateProfileBody("Ana Setter", "Setter", "Libero", photoKey);

        var put = await client.PutAsJsonAsync("/api/v1/profiles/me", request, Ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await put.Content.ReadFromJsonAsync<ProfileDto>(Ct);
        body!.DisplayName.Should().Be("Ana Setter");
        body.PrimaryPosition.Should().Be("Setter");
        body.SecondaryPosition.Should().Be("Libero");
        body.PhotoUrl.Should().Be($"https://s3.test.local/read/{photoKey}");

        // Persisted: a fresh GET returns the same values.
        var reread = await client.GetFromJsonAsync<ProfileDto>("/api/v1/profiles/me", Ct);
        reread!.DisplayName.Should().Be("Ana Setter");
        reread.PrimaryPosition.Should().Be("Setter");
        reread.SecondaryPosition.Should().Be("Libero");
    }

    /// <summary>Covers: F2.1 PUT validation — invalid position value returns 400.</summary>
    [Fact]
    public async Task PUT_me_with_invalid_position_returns_400()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);

        var request = new UpdateProfileBody("Valid Name", "Goalkeeper", null, null);
        var response = await client.PutAsJsonAsync("/api/v1/profiles/me", request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers: F2.1 PUT validation — secondary equal to primary returns 400.</summary>
    [Fact]
    public async Task PUT_me_with_secondary_equal_primary_returns_400()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);

        var request = new UpdateProfileBody("Valid Name", "Setter", "Setter", null);
        var response = await client.PutAsJsonAsync("/api/v1/profiles/me", request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers: F2.1 PUT validation — short display name returns 400.</summary>
    [Fact]
    public async Task PUT_me_with_short_display_name_returns_400()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);

        var request = new UpdateProfileBody("a", null, null, null);
        var response = await client.PutAsJsonAsync("/api/v1/profiles/me", request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Covers: F2.1 PUT validation — a photo key under another user's prefix is rejected (ownership guard).
    /// </summary>
    [Fact]
    public async Task PUT_me_with_foreign_photo_key_returns_400()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);

        var foreignKey = $"profiles/{Guid.NewGuid()}/photo/{Guid.NewGuid()}.jpg";
        var request = new UpdateProfileBody("Valid Name", null, null, foreignKey);
        var response = await client.PutAsJsonAsync("/api/v1/profiles/me", request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers: F2.1 PUT /profiles/me — 404 when the caller has no profile.</summary>
    [Fact]
    public async Task PUT_me_when_no_profile_returns_404()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var request = new UpdateProfileBody("Valid Name", null, null, null);
        var response = await client.PutAsJsonAsync("/api/v1/profiles/me", request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ═══ GET /profiles/{userId} ════════════════════════════════════════════════

    /// <summary>Covers: F2.1 GET /profiles/{userId} — returns another player's public profile.</summary>
    [Fact]
    public async Task GET_by_userId_returns_other_players_profile()
    {
        await _fx.ResetAsync();
        var target = Guid.NewGuid();
        await ProvisionAsync(target);

        var caller = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var profile = await caller.GetFromJsonAsync<ProfileDto>($"/api/v1/profiles/{target}", Ct);

        profile.Should().NotBeNull();
        profile!.UserId.Should().Be(target);
    }

    /// <summary>Covers: F2.1 GET /profiles/{userId} — 404 for an unknown user.</summary>
    [Fact]
    public async Task GET_by_userId_when_unknown_returns_404()
    {
        await _fx.ResetAsync();
        var caller = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await caller.GetAsync($"/api/v1/profiles/{Guid.NewGuid()}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ═══ POST /profiles/me/photo/upload-url ════════════════════════════════════

    /// <summary>
    /// Covers: F2.1 photo upload-url endpoint — 201 with an object key under the caller-owned prefix.
    /// </summary>
    [Fact]
    public async Task POST_photo_upload_url_returns_201_with_caller_prefixed_key()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        var client = _fx.CreateAuthenticatedClient(userId);

        var response = await client.PostAsJsonAsync(
            "/api/v1/profiles/me/photo/upload-url", new { ContentType = "image/png" }, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<UploadUrlDto>(Ct);
        body!.UploadUrl.Should().Be(ProfileWebApplicationFactory.StubUploadUrl);
        body.ObjectKey.Should().StartWith($"profiles/{userId}/photo/");
        body.ObjectKey.Should().EndWith(".png");
    }

    /// <summary>Covers: F2.1 photo upload-url — 400 for an unsupported content type.</summary>
    [Fact]
    public async Task POST_photo_upload_url_with_unsupported_content_type_returns_400()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.PostAsJsonAsync(
            "/api/v1/profiles/me/photo/upload-url", new { ContentType = "image/gif" }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers: F2.1 photo upload-url — 401 without a JWT.</summary>
    [Fact]
    public async Task POST_photo_upload_url_without_JWT_returns_401()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAnonymousClient();
        var response = await client.PostAsJsonAsync(
            "/api/v1/profiles/me/photo/upload-url", new { ContentType = "image/png" }, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ═══ Stats aggregation + level + idempotency ═══════════════════════════════

    /// <summary>
    /// Covers: F2.1 "aggregated stats" + "automatically calculated level" — after 10 finished matches
    /// (7 wins, 2 losses, 1 draw) with exactly one MVP, GET reports the exact tallies and level
    /// Intermediate (10 matches AND >=1 MVP). Also covers the recompute-not-increment idempotency:
    /// redelivering one match leaves the tallies unchanged.
    /// </summary>
    [Fact]
    public async Task Finished_matches_aggregate_stats_promote_level_and_are_idempotent()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);

        var matchIds = new List<Guid>();
        // 7 wins (match #0 is the MVP), 2 losses, 1 draw.
        for (var i = 0; i < 7; i++)
        {
            matchIds.Add(await ApplyMatchAsync(userId, "Win", wasMvp: i == 0, daysAgo: 30 - i));
        }
        for (var i = 0; i < 2; i++)
        {
            matchIds.Add(await ApplyMatchAsync(userId, "Loss", wasMvp: false, daysAgo: 20 - i));
        }
        matchIds.Add(await ApplyMatchAsync(userId, "Draw", wasMvp: false, daysAgo: 10));

        var client = _fx.CreateAuthenticatedClient(userId);
        var profile = await client.GetFromJsonAsync<ProfileDto>("/api/v1/profiles/me", Ct);

        profile!.Stats.Should().Be(new StatsDto(MatchesPlayed: 10, Wins: 7, Losses: 2, Draws: 1, MvpsReceived: 1));
        profile.Level.Should().Be("Intermediate");

        // Idempotency: redeliver the MVP win (same match id) → no drift.
        await ApplyMatchAsync(userId, "Win", wasMvp: true, daysAgo: 30, matchId: matchIds[0]);

        var reread = await client.GetFromJsonAsync<ProfileDto>("/api/v1/profiles/me", Ct);
        reread!.Stats.Should().Be(new StatsDto(10, 7, 2, 1, 1), "redelivery must not double-count");
        reread.Level.Should().Be("Intermediate");
    }

    /// <summary>
    /// Covers: F2.1 "automatically calculated level" boundary — 10 matches but zero MVPs stays Beginner.
    /// </summary>
    [Fact]
    public async Task Ten_matches_without_mvp_stays_Beginner()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);

        for (var i = 0; i < 10; i++)
        {
            await ApplyMatchAsync(userId, "Win", wasMvp: false, daysAgo: 40 - i);
        }

        var client = _fx.CreateAuthenticatedClient(userId);
        var profile = await client.GetFromJsonAsync<ProfileDto>("/api/v1/profiles/me", Ct);

        profile!.Stats.MatchesPlayed.Should().Be(10);
        profile.Stats.MvpsReceived.Should().Be(0);
        profile.Level.Should().Be("Beginner");
    }

    // ═══ Match history pagination ══════════════════════════════════════════════

    /// <summary>
    /// Covers: F2.1 "match history with pagination" — newest-first ordering, correct page slicing and
    /// HasNextPage across pages.
    /// </summary>
    [Fact]
    public async Task GET_match_history_paginates_newest_first()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);

        // 5 matches, oldest first in application order; match played "daysAgo" decreasing → newest last.
        var names = new[] { "M-oldest", "M-2", "M-3", "M-4", "M-newest" };
        for (var i = 0; i < names.Length; i++)
        {
            await ApplyMatchAsync(userId, "Win", wasMvp: false, daysAgo: 50 - i * 5, matchName: names[i]);
        }

        var client = _fx.CreateAuthenticatedClient(userId);

        var page1 = await client.GetFromJsonAsync<PagedDto<HistoryDto>>(
            "/api/v1/profiles/me/match-history?page=1&pageSize=2", Ct);
        page1!.TotalCount.Should().Be(5);
        page1.Page.Should().Be(1);
        page1.PageSize.Should().Be(2);
        page1.HasNextPage.Should().BeTrue();
        page1.Items.Should().HaveCount(2);
        page1.Items[0].MatchName.Should().Be("M-newest", "history is newest-first");
        page1.Items[1].MatchName.Should().Be("M-4");

        var page3 = await client.GetFromJsonAsync<PagedDto<HistoryDto>>(
            "/api/v1/profiles/me/match-history?page=3&pageSize=2", Ct);
        page3!.HasNextPage.Should().BeFalse();
        page3.Items.Should().HaveCount(1);
        page3.Items[0].MatchName.Should().Be("M-oldest");
    }

    /// <summary>Covers: F2.1 match-history — 400 when page/pageSize are out of range.</summary>
    [Theory]
    [InlineData("page=0&pageSize=20")]
    [InlineData("page=1&pageSize=0")]
    [InlineData("page=1&pageSize=51")]
    public async Task GET_match_history_with_out_of_range_paging_returns_400(string query)
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);

        var response = await client.GetAsync($"/api/v1/profiles/me/match-history?{query}", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers: F2.1 match-history — 404 when the caller has no profile.</summary>
    [Fact]
    public async Task GET_match_history_when_no_profile_returns_404()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.GetAsync("/api/v1/profiles/me/match-history", Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ═══ OUT: manual stat editing (structural absence) ═════════════════════════

    /// <summary>
    /// Covers: F2.1 "OUT: manual stat editing" — PUT /profiles/me changes only display name / positions
    /// / photo; the derived stats and level are left untouched by a profile update.
    /// </summary>
    [Fact]
    public async Task PUT_me_does_not_touch_stats_or_level()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        // Build a real stat/level state first (Intermediate).
        for (var i = 0; i < 10; i++)
        {
            await ApplyMatchAsync(userId, "Win", wasMvp: i == 0, daysAgo: 40 - i);
        }

        var client = _fx.CreateAuthenticatedClient(userId);
        var request = new UpdateProfileBody("Renamed Player", "Opposite", null, null);
        var put = await client.PutAsJsonAsync("/api/v1/profiles/me", request, Ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await put.Content.ReadFromJsonAsync<ProfileDto>(Ct);
        body!.Stats.Should().Be(new StatsDto(10, 10, 0, 0, 1), "the update must not alter derived stats");
        body.Level.Should().Be("Intermediate", "the update must not alter the derived level");
    }

    /// <summary>
    /// Covers: F2.1 "OUT: manual stat editing" — no route mutates stats/level/history; mutating verbs
    /// on the read-only match-history resource are not allowed.
    /// </summary>
    [Theory]
    [InlineData("POST")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task Mutating_verbs_on_match_history_are_not_available(string method)
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);

        var request = new HttpRequestMessage(new HttpMethod(method), "/api/v1/profiles/me/match-history");
        var response = await client.SendAsync(request, Ct);

        response.StatusCode.Should().BeOneOf(HttpStatusCode.MethodNotAllowed, HttpStatusCode.NotFound);
    }

    // ═══ Seeding helpers (drive the Background-Worker write path) ═══════════════

    private Task ProvisionAsync(Guid userId) =>
        _fx.WithScopeAsync(async sp =>
        {
            var provisioner = sp.GetRequiredService<IPlayerProfileProvisioner>();
            await provisioner.EnsureProfileAsync(userId, Ct);
        });

    private async Task<Guid> ApplyMatchAsync(
        Guid userId,
        string outcome,
        bool wasMvp,
        int daysAgo,
        string matchName = "Match",
        Guid? matchId = null)
    {
        var id = matchId ?? Guid.NewGuid();
        await _fx.WithScopeAsync(async sp =>
        {
            var writer = sp.GetRequiredService<IPlayerStatsWriter>();
            var participation = new FinishedMatchParticipation(
                PlayerId: userId,
                MatchId: id,
                MatchName: matchName,
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

    private sealed record ProfileDto(
        Guid UserId,
        string DisplayName,
        string? PrimaryPosition,
        string? SecondaryPosition,
        string? PhotoUrl,
        string Level,
        StatsDto Stats,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record StatsDto(
        int MatchesPlayed,
        int Wins,
        int Losses,
        int Draws,
        int MvpsReceived);

    private sealed record HistoryDto(
        Guid MatchId,
        string MatchName,
        DateTimeOffset MatchDateTime,
        Guid? TeamId,
        string Outcome,
        bool WasMvp,
        int? DurationSeconds);

    private sealed record PagedDto<T>(
        IReadOnlyList<T> Items,
        int Page,
        int PageSize,
        long TotalCount,
        bool HasNextPage);

    private sealed record UploadUrlDto(string UploadUrl, string ObjectKey, DateTimeOffset ExpiresAt);

    private sealed record UpdateProfileBody(
        string DisplayName,
        string? PrimaryPosition,
        string? SecondaryPosition,
        string? PhotoObjectKey);
}
