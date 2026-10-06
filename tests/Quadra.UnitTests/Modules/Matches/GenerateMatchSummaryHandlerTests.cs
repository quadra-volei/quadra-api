using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.Matches;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;
using MatchSummaryEntity = Quadra.Modules.Matches.Entities.MatchSummary;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>
/// Unit tests for <see cref="GenerateMatchSummaryHandler"/> (F1.6 — POST /matches/{id}/summary).
///
/// Covers the POST behavior steps and both acceptance criteria at the handler level:
///   - authorize (404 not found / 403 non-organizer),
///   - immutability guard (409 when a summary already exists + DbUpdateException race guard),
///   - InGame result preconditions (404 no scoreboard, 409 not Ended, 409 MVP voting Open),
///   - cancelled match (409),
///   - success: snapshot content, matches.status → Ended transition, event publication.
/// </summary>
public sealed class GenerateMatchSummaryHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GameStart = new(2026, 7, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset GameEnd = new(2026, 7, 1, 11, 30, 0, TimeSpan.Zero); // +5400s

    private static readonly Guid OrganizerId = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherUserId = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private static readonly Guid TeamA = new("0000000a-0000-0000-0000-000000000000");
    private static readonly Guid TeamB = new("0000000b-0000-0000-0000-000000000000");
    private static readonly Guid P1 = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid P2 = new("22222222-2222-2222-2222-222222222222");
    private static readonly Guid P3 = new("33333333-3333-3333-3333-333333333333");
    private static readonly Guid P4 = new("44444444-4444-4444-4444-444444444444");

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();
    private readonly IMatchSummaryRepository _summaryRepository = Substitute.For<IMatchSummaryRepository>();
    private readonly IMatchResultReader _resultReader = Substitute.For<IMatchResultReader>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);
    private readonly ILogger<GenerateMatchSummaryHandler> _logger =
        Substitute.For<ILogger<GenerateMatchSummaryHandler>>();

    private GenerateMatchSummaryHandler CreateSut() =>
        new(_matchRepository, _summaryRepository, _resultReader, _eventPublisher, _timeProvider, _logger);

    // ─── 404: match not found ─────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 POST step 2 — 404 when the match does not exist.
    /// </summary>
    [Fact]
    public async Task HandleAsync_when_match_not_found_throws_MatchNotFoundException()
    {
        var matchId = Guid.NewGuid();
        _matchRepository.FindByIdAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, OrganizerId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchNotFoundException>();
        await _summaryRepository.DidNotReceive().AddAsync(Arg.Any<MatchSummaryEntity>(), Arg.Any<CancellationToken>());
    }

    // ─── 403: non-organizer ───────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 POST step 3 — 403 when the caller is not the match organizer.
    /// </summary>
    [Fact]
    public async Task HandleAsync_when_caller_not_organizer_throws_MatchAccessDeniedException()
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);

        var act = async () => await CreateSut().HandleAsync(match.Id, OtherUserId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchAccessDeniedException>();
        await _summaryRepository.DidNotReceive().AddAsync(Arg.Any<MatchSummaryEntity>(), Arg.Any<CancellationToken>());
    }

    // ─── 409: summary already exists (immutable) ──────────────────────────────

    /// <summary>
    /// Covers: F1.6 AC "immutable record" + POST step 4 — a second generation for a match that
    /// already has a summary throws MatchSummaryAlreadyExistsException (409) and never re-persists.
    /// </summary>
    [Fact]
    public async Task HandleAsync_when_summary_already_exists_throws_AlreadyExists()
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _summaryRepository.ExistsForMatchAsync(match.Id, Arg.Any<CancellationToken>()).Returns(true);

        var act = async () => await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchSummaryAlreadyExistsException>();
        await _summaryRepository.DidNotReceive().AddAsync(Arg.Any<MatchSummaryEntity>(), Arg.Any<CancellationToken>());
        await _resultReader.DidNotReceive().GetMatchResultAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.6 immutability enforcement (d) — a unique-constraint DbUpdateException race on
    /// insert is mapped to MatchSummaryAlreadyExistsException (409), never surfacing a 500.
    /// </summary>
    [Fact]
    public async Task HandleAsync_when_insert_races_unique_constraint_throws_AlreadyExists()
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _summaryRepository.ExistsForMatchAsync(match.Id, Arg.Any<CancellationToken>()).Returns(false);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>()).Returns(BuildEndedResult());
        _summaryRepository.AddAsync(Arg.Any<MatchSummaryEntity>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new DbUpdateException("duplicate key"));

        var act = async () => await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchSummaryAlreadyExistsException>();
        await _eventPublisher.DidNotReceive().PublishAsync(Arg.Any<MatchSummaryGenerated>(), Arg.Any<CancellationToken>());
    }

    // ─── 404: no scoreboard ───────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 POST step 5 — 404 when no scoreboard exists for the match (reader returns null).
    /// </summary>
    [Fact]
    public async Task HandleAsync_when_no_scoreboard_throws_ScoreboardNotFoundForSummary()
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await act.Should().ThrowAsync<ScoreboardNotFoundForSummaryException>();
    }

    // ─── 409: scoreboard not Ended ────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 POST step 6 — 409 when the scoreboard has not reached the Ended state.
    /// </summary>
    [Theory]
    [InlineData("NotStarted")]
    [InlineData("InProgress")]
    public async Task HandleAsync_when_scoreboard_not_ended_throws_MatchNotEnded(string state)
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns(BuildEndedResult() with { ScoreboardState = state });

        var act = async () => await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchNotEndedException>();
        await _summaryRepository.DidNotReceive().AddAsync(Arg.Any<MatchSummaryEntity>(), Arg.Any<CancellationToken>());
    }

    // ─── 409: MVP voting still Open ───────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 POST step 7 — 409 when an MVP voting session is still Open.
    /// </summary>
    [Fact]
    public async Task HandleAsync_when_mvp_voting_open_throws_MvpVotingStillOpen()
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns(BuildEndedResult() with { MvpVotingState = "Open" });

        var act = async () => await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await act.Should().ThrowAsync<MvpVotingStillOpenException>();
    }

    // ─── 409: cancelled match ─────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 POST step 10 + "Match summaries for cancelled matches are out of scope" —
    /// a Cancelled match cannot be summarized (409).
    /// </summary>
    [Fact]
    public async Task HandleAsync_when_match_cancelled_throws_MatchNotEnded()
    {
        var match = BuildMatch(MatchStatus.Cancelled);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>()).Returns(BuildEndedResult());

        var act = async () => await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchNotEndedException>().WithMessage("*cancelled*");
        await _summaryRepository.DidNotReceive().AddAsync(Arg.Any<MatchSummaryEntity>(), Arg.Any<CancellationToken>());
    }

    // ─── Success: snapshot content ────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 AC "endpoint returning final score, duration, MVP, team composition" — the
    /// response snapshots per-set final score, sets-won, duration, MVP and team composition.
    /// </summary>
    [Fact]
    public async Task HandleAsync_success_returns_snapshot_with_score_duration_mvp_and_teams()
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>()).Returns(BuildEndedResult());

        var response = await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        response.MatchId.Should().Be(match.Id);
        response.Format.Should().Be("BestOf3");
        response.TeamASetsWon.Should().Be(2);
        response.TeamBSetsWon.Should().Be(1);
        response.WinnerTeamId.Should().Be(TeamA);

        // Duration precomputed from the scoreboard start/end (5400 seconds).
        response.DurationSeconds.Should().Be(5400);

        // Final score: per-set rows, ordered by set number ascending.
        response.Sets.Select(s => s.SetNumber).Should().ContainInOrder(1, 2, 3);
        response.Sets[0].TeamAPoints.Should().Be(25);
        response.Sets[0].TeamBPoints.Should().Be(20);
        response.Sets[0].WinnerTeamId.Should().Be(TeamA);

        // MVP snapshot.
        response.MvpPlayerId.Should().Be(P1);
        response.MvpVoteCount.Should().Be(3);
        response.MvpTotalVotes.Should().Be(4);

        // Team composition: teams ordered by name; player ids ordered ascending.
        response.Teams.Select(t => t.Name).Should().ContainInOrder("Team A", "Team B");
        response.Teams[0].PlayerIds.Should().ContainInOrder(P1, P2);
        response.Teams[1].PlayerIds.Should().ContainInOrder(P3, P4);
    }

    /// <summary>
    /// Covers: F1.6 POST steps 9-11 — a successful generation from a Closed match persists the
    /// summary and transitions matches.status to Ended.
    /// </summary>
    [Fact]
    public async Task HandleAsync_success_persists_summary_and_marks_match_ended()
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>()).Returns(BuildEndedResult());

        await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        match.Status.Should().Be(MatchStatus.Ended);
        match.UpdatedAt.Should().Be(FixedNow);
        await _summaryRepository.Received(1).AddAsync(
            Arg.Is<MatchSummaryEntity>(s => s.MatchId == match.Id && s.DurationSeconds == 5400),
            Arg.Any<CancellationToken>());
        await _matchRepository.Received(1).UpdateAsync(match, Arg.Any<CancellationToken>());
    }

    // ─── Success: event publication ───────────────────────────────────────────

    /// <summary>
    /// Covers: F1.6 POST step 12 — MatchSummaryGenerated is published (with participant ids and a
    /// non-null winner) AND MatchStatusChanged is published because the status actually changed.
    /// </summary>
    [Fact]
    public async Task HandleAsync_success_from_Closed_publishes_both_events()
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>()).Returns(BuildEndedResult());

        await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchSummaryGenerated>(e =>
                e.MatchId == match.Id
                && e.WinnerTeamId == TeamA
                && e.MvpPlayerId == P1
                && e.DurationSeconds == 5400
                && e.ParticipantPlayerIds.Count == 4
                && e.OccurredAt == FixedNow),
            Arg.Any<CancellationToken>());

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchStatusChanged>(e =>
                e.MatchId == match.Id
                && e.PreviousStatus == "Closed"
                && e.NewStatus == "Ended"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.6 POST step 12 — MatchStatusChanged is published ONLY when the status changed:
    /// when the match is already Ended, only MatchSummaryGenerated is published and no re-update runs.
    /// </summary>
    [Fact]
    public async Task HandleAsync_when_match_already_ended_does_not_publish_status_changed()
    {
        var match = BuildMatch(MatchStatus.Ended);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>()).Returns(BuildEndedResult());

        await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await _summaryRepository.Received(1).AddAsync(Arg.Any<MatchSummaryEntity>(), Arg.Any<CancellationToken>());
        await _matchRepository.DidNotReceive().UpdateAsync(Arg.Any<Match>(), Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(Arg.Any<MatchSummaryGenerated>(), Arg.Any<CancellationToken>());
        await _eventPublisher.DidNotReceive().PublishAsync(Arg.Any<MatchStatusChanged>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.6 "WinnerTeamId in the event" note — a level (no-winner) game maps the nullable
    /// winner to Guid.Empty in the MatchSummaryGenerated payload.
    /// </summary>
    [Fact]
    public async Task HandleAsync_level_game_publishes_empty_winner_guid_in_event()
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns(BuildEndedResult() with { WinnerTeamId = null });

        await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchSummaryGenerated>(e => e.WinnerTeamId == Guid.Empty),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.6 "events after commit" note — MatchSummaryGenerated is published only AFTER the
    /// summary is persisted (never before the AddAsync call succeeds).
    /// </summary>
    [Fact]
    public async Task HandleAsync_publishes_event_after_persisting_summary()
    {
        var match = BuildMatch(MatchStatus.Closed);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _resultReader.GetMatchResultAsync(match.Id, Arg.Any<CancellationToken>()).Returns(BuildEndedResult());

        var persisted = false;
        var publishedBeforePersist = false;

        _summaryRepository.AddAsync(Arg.Any<MatchSummaryEntity>(), Arg.Any<CancellationToken>())
            .Returns(_ => { persisted = true; return Task.CompletedTask; });
        _eventPublisher.PublishAsync(Arg.Any<MatchSummaryGenerated>(), Arg.Any<CancellationToken>())
            .Returns(_ => { if (!persisted) { publishedBeforePersist = true; } return Task.CompletedTask; });

        await CreateSut().HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        publishedBeforePersist.Should().BeFalse("MatchSummaryGenerated must be published only after the summary is persisted");
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Match BuildMatch(MatchStatus status)
    {
        var match = Match.Create(
            organizerId: OrganizerId,
            name: "Test Match",
            description: null,
            address: "Test Address",
            latitude: 0,
            longitude: 0,
            dateTime: FixedNow.AddDays(-1),
            maxPlayers: 10,
            regularSlots: 8,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: FixedNow.AddDays(-3),
            windowClosesAt: FixedNow.AddDays(-2),
            now: FixedNow.AddDays(-4));

        if (status != MatchStatus.Draft)
        {
            typeof(Match).GetProperty(nameof(Match.Status))!.SetValue(match, status);
        }

        return match;
    }

    /// <summary>
    /// An Ended BestOf3 result: Team A wins 2-1; per-set 25-20, 18-25, 25-23; MVP P1 (3/4 votes);
    /// Team A = {P1, P2}, Team B = {P3, P4}; duration 5400s.
    /// </summary>
    private static MatchResult BuildEndedResult() =>
        new(
            MatchId: Guid.NewGuid(),
            ScoreboardState: "Ended",
            Format: "BestOf3",
            TeamAId: TeamA,
            TeamBId: TeamB,
            TeamASetsWon: 2,
            TeamBSetsWon: 1,
            WinnerTeamId: TeamA,
            StartedAt: GameStart,
            EndedAt: GameEnd,
            Sets:
            [
                new MatchResultSet(1, 25, 20, TeamA),
                new MatchResultSet(2, 18, 25, TeamB),
                new MatchResultSet(3, 25, 23, TeamA),
            ],
            Teams:
            [
                // Deliberately unordered player ids to prove the mapping sorts them.
                new MatchResultTeam(TeamA, "Team A", [P2, P1]),
                new MatchResultTeam(TeamB, "Team B", [P4, P3]),
            ],
            MvpVotingState: "Closed",
            MvpPlayerId: P1,
            MvpVoteCount: 3,
            MvpTotalVotes: 4);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
