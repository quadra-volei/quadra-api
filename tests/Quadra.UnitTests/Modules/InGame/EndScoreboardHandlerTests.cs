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
/// Unit tests for <see cref="EndScoreboardHandler"/> — the force-end flow.
///
/// Covers (F1.4):
///   - Acceptance criterion "match state": a forced end transitions InProgress → Ended; the winner is
///     derived from sets won so far (null when equal); the current unfinished set is abandoned.
///   - Acceptance criterion "real-time": a successful end broadcasts NotifyScoreboardUpdatedAsync.
///   - MatchEnded SQS event is published on end.
///   - 404 match / 403 non-organizer / 404 scoreboard / 409 when not InProgress.
/// </summary>
public sealed class EndScoreboardHandlerTests
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

    private EndScoreboardHandler CreateSut() =>
        new(_matchReader, _scoreboardRepository, _eventPublisher, _notifier, _timeProvider,
            NullLogger<EndScoreboardHandler>.Instance);

    private void SetupMatch(Guid matchId, Guid organizerId, string status = "Closed") =>
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummary(matchId, organizerId, status));

    private Scoreboard SetupInProgress(Guid matchId)
    {
        var scoreboard = Scoreboard.Create(matchId, ScoreboardFormat.BestOf3, TeamA, TeamB, FixedNow);
        scoreboard.Start(FixedNow);
        scoreboard.OpenSet(1, isDecidingSet: false, FixedNow);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(scoreboard);
        SetupMatch(matchId, Organizer);
        return scoreboard;
    }

    // ─── error paths ─────────────────────────────────────────────────────────

    /// <summary>Covers: F1.4 — 404 when match not found.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotFound_when_match_missing()
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

    /// <summary>Covers: F1.4 — 404 when no scoreboard exists.</summary>
    [Fact]
    public async Task HandleAsync_throws_ScoreboardNotFound_when_scoreboard_missing()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<ScoreboardNotFoundException>();
    }

    /// <summary>Covers: F1.4 "match state" — 409 when the game is not InProgress (e.g. NotStarted).</summary>
    [Fact]
    public async Task HandleAsync_throws_InvalidState_when_not_in_progress()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        var scoreboard = Scoreboard.Create(matchId, ScoreboardFormat.BestOf3, TeamA, TeamB, FixedNow); // NotStarted
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(scoreboard);

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidScoreboardStateException>()
            .WithMessage("*in-progress*");
    }

    // ─── success ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.4 "match state" — forced end with a set lead ends the game with that team as winner
    /// and abandons the current unfinished set (winner null).
    /// </summary>
    [Fact]
    public async Task HandleAsync_ends_with_leading_team_as_winner_and_abandons_current_set()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId);
        // Simulate A having won set 1 already, currently mid set 2.
        scoreboard.Sets.First(s => s.SetNumber == 1).Finish(TeamA, FixedNow);
        scoreboard.RegisterSetWon(TeamA);
        scoreboard.OpenSet(2, isDecidingSet: false, FixedNow);

        var response = await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);

        scoreboard.State.Should().Be(ScoreboardState.Ended);
        response.State.Should().Be("Ended");
        response.WinnerTeamId.Should().Be(TeamA);
        scoreboard.Sets.First(s => s.SetNumber == 2).Status.Should().Be(SetStatus.Finished);
        scoreboard.Sets.First(s => s.SetNumber == 2).WinnerTeamId.Should().BeNull("an abandoned set has no winner");
    }

    /// <summary>Covers: F1.4 "match state" — forced end with equal sets won leaves the winner null.</summary>
    [Fact]
    public async Task HandleAsync_ends_with_null_winner_when_sets_equal()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId); // 0-0 sets

        var response = await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);

        scoreboard.State.Should().Be(ScoreboardState.Ended);
        response.WinnerTeamId.Should().BeNull();
    }

    /// <summary>Covers: F1.4 "real-time" — a successful end broadcasts NotifyScoreboardUpdatedAsync.</summary>
    [Fact]
    public async Task HandleAsync_broadcasts_scoreboard_updated()
    {
        var matchId = Guid.NewGuid();
        SetupInProgress(matchId);

        await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);

        await _notifier.Received(1).NotifyScoreboardUpdatedAsync(
            Arg.Is<ScoreboardUpdatedMessage>(m => m.MatchId == matchId && m.State == "Ended"),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: F1.4 — MatchEnded SQS event is published on forced end.</summary>
    [Fact]
    public async Task HandleAsync_publishes_MatchEnded_event()
    {
        var matchId = Guid.NewGuid();
        SetupInProgress(matchId);

        await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchEnded>(e => e.MatchId == matchId),
            Arg.Any<CancellationToken>());
    }
}
