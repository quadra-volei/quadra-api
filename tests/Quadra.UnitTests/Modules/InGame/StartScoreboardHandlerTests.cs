using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.InGame;
using Quadra.Shared.Realtime;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="StartScoreboardHandler"/>.
///
/// Covers (F1.4):
///   - Acceptance criterion "match state" — start transitions NotStarted → InProgress and opens set 1.
///   - Acceptance criterion "real-time" — a successful start broadcasts via NotifyScoreboardUpdatedAsync.
///   - MatchStarted SQS event is published on start.
///   - 404 when match not found; 403 when caller is not organizer.
///   - 404 when no scoreboard exists.
///   - 409 (invalid transition) when the scoreboard is not NotStarted.
/// </summary>
public sealed class StartScoreboardHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizer = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Other = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeamA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeamB = new("22222222-2222-2222-2222-222222222222");

    private readonly IMatchReader _matchReader = Substitute.For<IMatchReader>();
    private readonly IScoreboardRepository _scoreboardRepository = Substitute.For<IScoreboardRepository>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private readonly IMatchRoomNotifier _notifier = Substitute.For<IMatchRoomNotifier>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private StartScoreboardHandler CreateSut() =>
        new(_matchReader, _scoreboardRepository, _eventPublisher, _notifier, _timeProvider,
            NullLogger<StartScoreboardHandler>.Instance);

    private void SetupMatch(Guid matchId, Guid organizerId, string status = "Closed") =>
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummary(matchId, organizerId, status));

    private Scoreboard NewScoreboard(Guid matchId) =>
        Scoreboard.Create(matchId, ScoreboardFormat.BestOf3, TeamA, TeamB, FixedNow);

    // ─── 404 / 403 ───────────────────────────────────────────────────────────

    /// <summary>Covers: F1.4 — 404 when match not found.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotFound_when_match_does_not_exist()
    {
        var matchId = Guid.NewGuid();
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    /// <summary>Covers: F1.4 — 403 when caller is not the organizer.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchAccessDenied_when_caller_is_not_organizer()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);

        var act = async () => await CreateSut().HandleAsync(matchId, Other, CancellationToken.None);
        await act.Should().ThrowAsync<MatchAccessDeniedException>();
    }

    /// <summary>Covers: F1.4 — 404 when no scoreboard exists for the match.</summary>
    [Fact]
    public async Task HandleAsync_throws_ScoreboardNotFound_when_scoreboard_missing()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<ScoreboardNotFoundException>();
    }

    // ─── 409: invalid transition ─────────────────────────────────────────────

    /// <summary>Covers: F1.4 "match state" — 409 when starting an already-started game.</summary>
    [Fact]
    public async Task HandleAsync_throws_InvalidState_when_already_started()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        var scoreboard = NewScoreboard(matchId);
        scoreboard.Start(FixedNow); // already InProgress
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(scoreboard);

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidScoreboardStateException>()
            .WithMessage("*already started*");
    }

    // ─── success ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.4 "match state" — start transitions to InProgress, sets current set number to 1,
    /// and opens the first (non-deciding) set.
    /// </summary>
    [Fact]
    public async Task HandleAsync_transitions_to_InProgress_and_opens_set_1()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        var scoreboard = NewScoreboard(matchId);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(scoreboard);

        var response = await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);

        response.State.Should().Be("InProgress");
        response.CurrentSetNumber.Should().Be(1);
        response.StartedAt.Should().Be(FixedNow);
        response.Sets.Should().ContainSingle();
        response.Sets[0].SetNumber.Should().Be(1);
        response.Sets[0].IsDecidingSet.Should().BeFalse();
        response.Sets[0].Status.Should().Be("InProgress");

        await _scoreboardRepository.Received(1).UpdateAsync(scoreboard, Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: F1.4 "real-time" — a successful start broadcasts NotifyScoreboardUpdatedAsync.</summary>
    [Fact]
    public async Task HandleAsync_broadcasts_scoreboard_updated()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(NewScoreboard(matchId));

        await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);

        await _notifier.Received(1).NotifyScoreboardUpdatedAsync(
            Arg.Is<ScoreboardUpdatedMessage>(m => m.MatchId == matchId && m.State == "InProgress"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: F1.4 — MatchStarted SQS event is published on start.</summary>
    [Fact]
    public async Task HandleAsync_publishes_MatchStarted_event()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(NewScoreboard(matchId));

        await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchStarted>(e =>
                e.MatchId == matchId && e.TeamAId == TeamA && e.TeamBId == TeamB && e.Format == "BestOf3"),
            Arg.Any<CancellationToken>());
    }
}
