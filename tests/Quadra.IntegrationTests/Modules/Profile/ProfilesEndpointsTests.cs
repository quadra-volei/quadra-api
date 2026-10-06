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
    /// Covers: F2.1 "provisioning" + defaults — after provisioning, GET returns the empty shell:
    /// placeholder display name, no handle/position/photo, Beginner level, onboarding not
    /// completed, base skill ratings and all-zero stats.
    /// </summary>
    [Fact]
    public async Task GET_me_after_provision_returns_the_empty_shell()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);

        var client = _fx.CreateAuthenticatedClient(userId);
        var profile = await client.GetFromJsonAsync<ProfileDto>("/api/v1/profiles/me", Ct);

        profile.Should().NotBeNull();
        profile!.UserId.Should().Be(userId);
        profile.DisplayName.Should().Be("Player");
        profile.FirstName.Should().BeEmpty();
        profile.Handle.Should().BeNull();
        profile.Position.Should().BeNull();
        profile.PhotoUrl.Should().BeNull();
        profile.Level.Should().Be("Beginner");
        profile.OnboardingCompleted.Should().BeFalse();
        profile.Skills.Should().Be(new SkillsDto(Overall: 50, Ace: 50, Block: 50, Attack: 50, Defense: 50));
        profile.Stats.Should().Be(new StatsDto(0, 0, 0, 0, 0));
    }

    // ═══ PUT /profiles/me ══════════════════════════════════════════════════════

    /// <summary>
    /// Covers: F2.1 onboarding — the first PUT stores name, surname, handle (lowercased), birth
    /// date, position, modality and the self-declared level, marks onboarding complete, derives
    /// the skill ratings from level + position, and resolves the photo URL. GET returns the same.
    /// </summary>
    [Fact]
    public async Task PUT_me_completes_onboarding_with_the_mobile_fields()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);

        var photoKey = $"profiles/{userId}/photo/{Guid.NewGuid()}.jpg";
        var request = Onboarding(handle: "Ana_Lev", position: "LEV", level: "Intermediate") with
        {
            PhotoObjectKey = photoKey,
        };

        var put = await client.PutAsJsonAsync("/api/v1/profiles/me", request, Ct);
        put.StatusCode.Should().Be(HttpStatusCode.OK);

        var reread = await client.GetFromJsonAsync<ProfileDto>("/api/v1/profiles/me", Ct);
        foreach (var body in new[] { await put.Content.ReadFromJsonAsync<ProfileDto>(Ct), reread })
        {
            body!.FirstName.Should().Be("Ana");
            body.LastName.Should().Be("Souza");
            body.DisplayName.Should().Be("Ana Souza");
            body.Handle.Should().Be("ana_lev");
            body.BirthDate.Should().Be(new DateOnly(1998, 3, 14));
            body.Position.Should().Be("LEV");
            body.Modality.Should().Be("Indoor");
            body.DeclaredLevel.Should().Be("Intermediate");
            body.Level.Should().Be("Intermediate");
            body.OnboardingCompleted.Should().BeTrue();
            // Intermediate base 60; LEV: ACE +4, DEF +4.
            body.Skills.Should().Be(new SkillsDto(Overall: 62, Ace: 64, Block: 60, Attack: 60, Defense: 64));
            body.PhotoUrl.Should().Be($"https://s3.test.local/read/{photoKey}");
            body.PhotoObjectKey.Should().Be(photoKey);
        }
    }

    /// <summary>
    /// Covers: F2.1 onboarding — the first PUT must carry the modality and the declared level.
    /// </summary>
    [Theory]
    [InlineData(null, "Beginner")]
    [InlineData("Indoor", null)]
    public async Task PUT_me_without_onboarding_fields_returns_400(string? modality, string? level)
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);

        var request = Onboarding() with { Modality = modality, Level = level };
        var response = await client.PutAsJsonAsync("/api/v1/profiles/me", request, Ct);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var profile = await client.GetFromJsonAsync<ProfileDto>("/api/v1/profiles/me", Ct);
        profile!.OnboardingCompleted.Should().BeFalse();
    }

    /// <summary>
    /// Covers: F2.1 edit (S10) — after onboarding, a PUT without modality/level edits the profile
    /// and keeps them; trying to re-declare the level is rejected.
    /// </summary>
    [Fact]
    public async Task PUT_me_after_onboarding_edits_the_profile_but_cannot_redeclare_the_level()
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);
        (await client.PutAsJsonAsync("/api/v1/profiles/me", Onboarding(level: "Beginner"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var edit = Onboarding(handle: "ana_new", position: "LIB") with
        {
            LastName = "Lima",
            Modality = null,
            Level = null,
        };
        var edited = await client.PutAsJsonAsync("/api/v1/profiles/me", edit, Ct);
        edited.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await edited.Content.ReadFromJsonAsync<ProfileDto>(Ct);
        body!.LastName.Should().Be("Lima");
        body.Handle.Should().Be("ana_new");
        body.Position.Should().Be("LIB");
        body.Modality.Should().Be("Indoor");
        body.DeclaredLevel.Should().Be("Beginner");
        // Beginner base 50; LIB: DEF +10, ATA -5, BLK -5.
        body.Skills.Should().Be(new SkillsDto(Overall: 50, Ace: 50, Block: 45, Attack: 45, Defense: 60));

        var redeclare = await client.PutAsJsonAsync(
            "/api/v1/profiles/me", edit with { Level = "Advanced" }, Ct);
        redeclare.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>
    /// Covers: F2.1 "handle uniqueness (case-insensitive)" — a handle owned by another player is
    /// refused with 409 whatever its casing; keeping one's own handle is fine.
    /// </summary>
    [Fact]
    public async Task PUT_me_with_a_handle_taken_by_another_player_returns_409()
    {
        await _fx.ResetAsync();
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await ProvisionAsync(first);
        await ProvisionAsync(second);
        var firstClient = _fx.CreateAuthenticatedClient(first);
        var secondClient = _fx.CreateAuthenticatedClient(second);

        (await firstClient.PutAsJsonAsync("/api/v1/profiles/me", Onboarding(handle: "craque"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var taken = await secondClient.PutAsJsonAsync("/api/v1/profiles/me", Onboarding(handle: "CRAQUE"), Ct);
        taken.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var ownAgain = await firstClient.PutAsJsonAsync(
            "/api/v1/profiles/me", Onboarding(handle: "craque") with { Modality = null, Level = null }, Ct);
        ownAgain.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// Covers: F2.1 "availability-check endpoint" — free handle → available; another player's →
    /// not available (case-insensitive); the caller's own → available; malformed → 400.
    /// </summary>
    [Fact]
    public async Task GET_handle_availability_reports_free_taken_own_and_invalid()
    {
        await _fx.ResetAsync();
        var owner = Guid.NewGuid();
        var other = Guid.NewGuid();
        await ProvisionAsync(owner);
        await ProvisionAsync(other);
        var ownerClient = _fx.CreateAuthenticatedClient(owner);
        var otherClient = _fx.CreateAuthenticatedClient(other);
        (await ownerClient.PutAsJsonAsync("/api/v1/profiles/me", Onboarding(handle: "saque_viagem"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        const string url = "/api/v1/profiles/handle-availability?handle=";

        (await otherClient.GetFromJsonAsync<HandleAvailabilityDto>(url + "livre_123", Ct))
            .Should().Be(new HandleAvailabilityDto("livre_123", true));
        (await otherClient.GetFromJsonAsync<HandleAvailabilityDto>(url + "Saque_Viagem", Ct))
            .Should().Be(new HandleAvailabilityDto("saque_viagem", false));
        (await ownerClient.GetFromJsonAsync<HandleAvailabilityDto>(url + "saque_viagem", Ct))
            .Should().Be(new HandleAvailabilityDto("saque_viagem", true));
        (await otherClient.GetAsync(url + "a!", Ct)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await otherClient.GetAsync("/api/v1/profiles/handle-availability", Ct))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers: F2.1 PUT validation — each malformed field returns 400.</summary>
    [Theory]
    [InlineData("position", "Setter")]
    [InlineData("position", "lev")]
    [InlineData("handle", "ab")]
    [InlineData("handle", "com espaço")]
    [InlineData("firstName", "  ")]
    [InlineData("modality", "Grass")]
    [InlineData("level", "Elite")]
    [InlineData("birthDate", "2999-01-01")]
    public async Task PUT_me_with_an_invalid_field_returns_400(string field, string value)
    {
        await _fx.ResetAsync();
        var userId = Guid.NewGuid();
        await ProvisionAsync(userId);
        var client = _fx.CreateAuthenticatedClient(userId);

        var valid = Onboarding();
        var request = field switch
        {
            "position" => valid with { Position = value },
            "handle" => valid with { Handle = value },
            "firstName" => valid with { FirstName = value },
            "modality" => valid with { Modality = value },
            "level" => valid with { Level = value },
            "birthDate" => valid with { BirthDate = DateOnly.Parse(value, System.Globalization.CultureInfo.InvariantCulture) },
            _ => throw new ArgumentOutOfRangeException(nameof(field)),
        };

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
        var request = Onboarding() with { PhotoObjectKey = foreignKey };
        var response = await client.PutAsJsonAsync("/api/v1/profiles/me", request, Ct);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    /// <summary>Covers: F2.1 PUT /profiles/me — 404 when the caller has no profile.</summary>
    [Fact]
    public async Task PUT_me_when_no_profile_returns_404()
    {
        await _fx.ResetAsync();
        var client = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var response = await client.PutAsJsonAsync("/api/v1/profiles/me", Onboarding(), Ct);
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Covers: F2.1 privacy — another player's profile is public except for the birth date,
    /// which only its owner sees.
    /// </summary>
    [Fact]
    public async Task GET_by_userId_hides_the_birth_date_from_other_players()
    {
        await _fx.ResetAsync();
        var target = Guid.NewGuid();
        await ProvisionAsync(target);
        var owner = _fx.CreateAuthenticatedClient(target);
        (await owner.PutAsJsonAsync("/api/v1/profiles/me", Onboarding(handle: "reservado"), Ct))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        var stranger = _fx.CreateAuthenticatedClient(Guid.NewGuid());
        var seenByStranger = await stranger.GetFromJsonAsync<ProfileDto>($"/api/v1/profiles/{target}", Ct);
        var seenByOwner = await owner.GetFromJsonAsync<ProfileDto>($"/api/v1/profiles/{target}", Ct);

        seenByStranger!.Handle.Should().Be("reservado");
        seenByStranger.BirthDate.Should().BeNull();
        seenByStranger.PhotoObjectKey.Should().BeNull();
        seenByOwner!.BirthDate.Should().Be(new DateOnly(1998, 3, 14));
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
        // Declaring a lower level than the one already earned must not downgrade the player.
        var request = Onboarding(position: "OPO", level: "Beginner");
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
        string FirstName,
        string LastName,
        string? Handle,
        DateOnly? BirthDate,
        string? Position,
        string? Modality,
        string? DeclaredLevel,
        string Level,
        string? PhotoUrl,
        string? PhotoObjectKey,
        bool OnboardingCompleted,
        SkillsDto Skills,
        StatsDto Stats,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt);

    private sealed record SkillsDto(int Overall, int Ace, int Block, int Attack, int Defense);

    private sealed record HandleAvailabilityDto(string Handle, bool Available);

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
        string FirstName,
        string LastName,
        string Handle,
        DateOnly BirthDate,
        string Position,
        string? Modality,
        string? Level,
        string? PhotoObjectKey);

    /// <summary>A valid onboarding request; tests override single fields with <c>with</c>.</summary>
    private static UpdateProfileBody Onboarding(
        string handle = "ana_souza",
        string position = "PON",
        string level = "Beginner") =>
        new(
            FirstName: "Ana",
            LastName: "Souza",
            Handle: handle,
            BirthDate: new DateOnly(1998, 3, 14),
            Position: position,
            Modality: "Indoor",
            Level: level,
            PhotoObjectKey: null);
}
