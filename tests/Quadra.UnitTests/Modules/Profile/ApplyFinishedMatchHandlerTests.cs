using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quadra.Modules.Profile.Abstractions;
using Quadra.Modules.Profile.Application;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.UnitTests.Modules.Profile;

/// <summary>
/// Unit tests for <see cref="ApplyFinishedMatchHandler"/> (implements <c>IPlayerStatsWriter</c>).
///
/// Covers: F2.1 AC "aggregated stats: matches played, wins, losses, draws, MVPs received" and the
/// recompute-not-increment idempotency contract:
///   - the finished-match history row is upserted (idempotent on user_id + match_id);
///   - stats are recomputed from the history outcome counts, not incremented;
///   - level is recomputed from the fresh stats;
///   - when no profile exists yet the pair is bootstrapped defensively.
/// </summary>
public sealed class ApplyFinishedMatchHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid MatchId = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TeamId = new("33333333-3333-3333-3333-333333333333");

    private readonly IPlayerProfileRepository _profiles = Substitute.For<IPlayerProfileRepository>();
    private readonly IPlayerMatchHistoryRepository _history = Substitute.For<IPlayerMatchHistoryRepository>();
    private readonly IPlayerCardRepository _cards = Substitute.For<IPlayerCardRepository>();
    private readonly TimeProvider _time = new FixedTimeProvider(Now);

    public ApplyFinishedMatchHandlerTests()
    {
        // Default: no card yet exists for the player (the handler will Generate on first crossing).
        _cards.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new PlayerCardLookup(Card: null, ProfileExists: true, MatchesPlayed: 0));
    }

    private ApplyFinishedMatchHandler CreateSut() =>
        new(_profiles, _history, _cards, _time, NullLogger<ApplyFinishedMatchHandler>.Instance);

    private static FinishedMatchParticipation Participation(string outcome = "Win", bool wasMvp = false) =>
        new(
            PlayerId: UserId,
            MatchId: MatchId,
            MatchName: "Sunday Volleyball",
            MatchDateTime: Now.AddDays(-1),
            TeamId: TeamId,
            Outcome: outcome,
            WasMvp: wasMvp,
            DurationSeconds: 3600,
            FinishedAt: Now);

    private (PlayerProfile Profile, PlayerStats Stats) SeedExistingProfile(
        int wins = 0, int losses = 0, int draws = 0, int mvps = 0)
    {
        var profile = PlayerProfile.Provision(UserId, Now.AddDays(-30));
        var stats = PlayerStats.Empty(UserId, Now.AddDays(-30));
        stats.Recompute(wins, losses, draws, mvps, Now.AddDays(-30));
        _profiles.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new ProfileWithStats(profile, stats));
        return (profile, stats);
    }

    // ─── History upsert ──────────────────────────────────────────────────────

    /// <summary>Covers: the finished match is written to history via an idempotent upsert.</summary>
    [Fact]
    public async Task ApplyFinishedMatchAsync_upserts_the_history_row()
    {
        SeedExistingProfile();
        _history.GetOutcomeCountsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new OutcomeCounts(1, 0, 0, 0));

        await CreateSut().ApplyFinishedMatchAsync(Participation(outcome: "Win"), CancellationToken.None);

        await _history.Received(1).UpsertAsync(
            Arg.Is<PlayerMatchHistoryEntry>(e =>
                e.UserId == UserId &&
                e.MatchId == MatchId &&
                e.Outcome == MatchOutcome.Win &&
                e.TeamId == TeamId),
            Arg.Any<CancellationToken>());
    }

    // ─── Recompute (not increment) ───────────────────────────────────────────

    /// <summary>
    /// Covers: stats are OVERWRITTEN from the freshly computed outcome counts, not incremented — the
    /// heart of idempotency. Pre-seeded stats of (5,5,5,5) are replaced by the repo's (3,1,1,1).
    /// </summary>
    [Fact]
    public async Task ApplyFinishedMatchAsync_recomputes_stats_from_counts_not_increment()
    {
        var (profile, stats) = SeedExistingProfile(wins: 5, losses: 5, draws: 5, mvps: 5);
        _history.GetOutcomeCountsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new OutcomeCounts(3, 1, 1, 1));

        await CreateSut().ApplyFinishedMatchAsync(Participation(), CancellationToken.None);

        stats.Wins.Should().Be(3);
        stats.Losses.Should().Be(1);
        stats.Draws.Should().Be(1);
        stats.MvpsReceived.Should().Be(1);
        stats.MatchesPlayed.Should().Be(5, "matches_played is the sum of the outcome counts");
        profile.Level.Should().Be(PlayerLevel.Beginner);
        await _profiles.Received(1).UpdateAsync(profile, Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: level is recomputed — crossing 10 matches with an MVP promotes to Intermediate.</summary>
    [Fact]
    public async Task ApplyFinishedMatchAsync_promotes_level_to_Intermediate()
    {
        var (profile, _) = SeedExistingProfile();
        _history.GetOutcomeCountsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new OutcomeCounts(8, 1, 1, 1)); // 10 matches, 1 mvp

        await CreateSut().ApplyFinishedMatchAsync(Participation(wasMvp: true), CancellationToken.None);

        profile.Level.Should().Be(PlayerLevel.Intermediate);
    }

    // ─── Defensive bootstrap ─────────────────────────────────────────────────

    /// <summary>Covers: when no profile exists yet the handler bootstraps the profile + stats pair.</summary>
    [Fact]
    public async Task ApplyFinishedMatchAsync_when_no_profile_bootstraps_it()
    {
        _profiles.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns((ProfileWithStats?)null);
        _history.GetOutcomeCountsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new OutcomeCounts(1, 0, 0, 0));

        await CreateSut().ApplyFinishedMatchAsync(Participation(), CancellationToken.None);

        await _profiles.Received(1).AddAsync(
            Arg.Is<PlayerProfile>(p => p.UserId == UserId),
            Arg.Is<PlayerStats>(s => s.UserId == UserId && s.Wins == 1 && s.MatchesPlayed == 1),
            Arg.Any<CancellationToken>());
    }

    // ─── Outcome parsing ─────────────────────────────────────────────────────

    /// <summary>Covers: each valid outcome string maps to the matching enum on the history row.</summary>
    [Theory]
    [InlineData("Win", MatchOutcome.Win)]
    [InlineData("Loss", MatchOutcome.Loss)]
    [InlineData("Draw", MatchOutcome.Draw)]
    public async Task ApplyFinishedMatchAsync_maps_outcome_string(string outcome, MatchOutcome expected)
    {
        SeedExistingProfile();
        _history.GetOutcomeCountsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new OutcomeCounts(0, 0, 0, 0));

        await CreateSut().ApplyFinishedMatchAsync(Participation(outcome: outcome), CancellationToken.None);

        await _history.Received(1).UpsertAsync(
            Arg.Is<PlayerMatchHistoryEntry>(e => e.Outcome == expected),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: an unrecognized outcome string is rejected.</summary>
    [Fact]
    public async Task ApplyFinishedMatchAsync_with_unknown_outcome_throws()
    {
        var act = async () => await CreateSut()
            .ApplyFinishedMatchAsync(Participation(outcome: "Forfeit"), CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    // ─── F2.2 Player-card generation gate ────────────────────────────────────

    /// <summary>
    /// Covers: F2.2 "generated only after 3 recorded matches (GenerationThreshold = 3); below 3 → no
    /// card row". With only 2 matches played the handler must not write any card row.
    /// </summary>
    [Theory]
    [InlineData(0, 0, 0, 0)] // 0 matches
    [InlineData(1, 0, 0, 0)] // 1 match
    [InlineData(1, 1, 0, 0)] // 2 matches
    public async Task ApplyFinishedMatchAsync_below_threshold_writes_no_card(
        int wins, int losses, int draws, int mvps)
    {
        SeedExistingProfile();
        _history.GetOutcomeCountsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new OutcomeCounts(wins, losses, draws, mvps));

        await CreateSut().ApplyFinishedMatchAsync(Participation(), CancellationToken.None);

        await _cards.DidNotReceive().UpsertAsync(Arg.Any<PlayerCard>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F2.2 "at/after 3 → card exists" plus "generated_at is set on first crossing". At exactly
    /// the threshold, with no existing card, a fresh card is generated whose snapshot mirrors the
    /// recomputed profile/stats and whose generated_at == refreshed_at == now.
    /// </summary>
    [Fact]
    public async Task ApplyFinishedMatchAsync_at_threshold_generates_card_snapshot()
    {
        var (profile, _) = SeedExistingProfile();
        _history.GetOutcomeCountsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new OutcomeCounts(2, 1, 0, 1)); // 3 matches

        await CreateSut().ApplyFinishedMatchAsync(Participation(), CancellationToken.None);

        await _cards.Received(1).UpsertAsync(
            Arg.Is<PlayerCard>(c =>
                c.UserId == UserId &&
                c.DisplayName == profile.DisplayName &&
                c.Level == profile.Level &&
                c.MatchesPlayed == 3 &&
                c.Wins == 2 &&
                c.Losses == 1 &&
                c.Draws == 0 &&
                c.MvpsReceived == 1 &&
                c.GeneratedAt == Now &&
                c.RefreshedAt == Now),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F2.2 "generated_at is set once on first crossing and preserved across refreshes;
    /// refreshed_at updates each finished match". An existing card (generated earlier) is refreshed:
    /// generated_at is preserved, refreshed_at advances to now, and the counters are overwritten.
    /// </summary>
    [Fact]
    public async Task ApplyFinishedMatchAsync_with_existing_card_refreshes_preserving_generated_at()
    {
        var (profile, _) = SeedExistingProfile();
        var generatedAt = Now.AddDays(-5);
        var existing = PlayerCard.Generate(
            UserId, profile.DisplayName, null, null, PlayerLevel.Beginner,
            matchesPlayed: 3, wins: 3, losses: 0, draws: 0, mvpsReceived: 0, generatedAt);
        _cards.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new PlayerCardLookup(existing, ProfileExists: true, MatchesPlayed: 3));
        _history.GetOutcomeCountsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new OutcomeCounts(5, 1, 0, 2)); // 6 matches now

        await CreateSut().ApplyFinishedMatchAsync(Participation(), CancellationToken.None);

        existing.GeneratedAt.Should().Be(generatedAt, "generated_at is set once and never overwritten");
        existing.RefreshedAt.Should().Be(Now, "refreshed_at updates on each finished match");
        existing.MatchesPlayed.Should().Be(6, "the snapshot is recomputed, not incremented");
        existing.Wins.Should().Be(5);
        existing.MvpsReceived.Should().Be(2);
        await _cards.Received(1).UpsertAsync(existing, Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F2.2 idempotency — "a redelivered MatchSummaryGenerated reproduces the identical card
    /// snapshot (recompute-not-increment)". Applying the same participation twice (same recomputed
    /// counts, existing card preserved) yields identical snapshot counters and generated_at both times.
    /// </summary>
    [Fact]
    public async Task ApplyFinishedMatchAsync_redelivery_reproduces_identical_card_snapshot()
    {
        var (profile, _) = SeedExistingProfile();
        var generatedAt = Now.AddDays(-1);
        var existing = PlayerCard.Generate(
            UserId, profile.DisplayName, null, null, PlayerLevel.Beginner,
            matchesPlayed: 3, wins: 2, losses: 1, draws: 0, mvpsReceived: 1, generatedAt);
        _cards.FindByUserIdAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new PlayerCardLookup(existing, ProfileExists: true, MatchesPlayed: 3));
        // Recompute-not-increment: the same event always yields the same counts.
        _history.GetOutcomeCountsAsync(UserId, Arg.Any<CancellationToken>())
            .Returns(new OutcomeCounts(2, 1, 0, 1));

        var sut = CreateSut();
        await sut.ApplyFinishedMatchAsync(Participation(), CancellationToken.None);
        await sut.ApplyFinishedMatchAsync(Participation(), CancellationToken.None); // redelivery

        existing.GeneratedAt.Should().Be(generatedAt, "redelivery must not move generated_at");
        existing.MatchesPlayed.Should().Be(3);
        existing.Wins.Should().Be(2);
        existing.Losses.Should().Be(1);
        existing.Draws.Should().Be(0);
        existing.MvpsReceived.Should().Be(1);
    }
}
