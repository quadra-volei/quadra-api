using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.InGame;
using Quadra.Shared.Realtime;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="RecordPointHandler"/> — the real-time point/set recording flow.
///
/// Covers (F1.4):
///   - Acceptance criterion "set-by-set scoreboard": a point increments the current set; a set
///     auto-finishes at 25 (15 deciding) with a win-by-2 margin and no cap; the game ends when a
///     team reaches SetsToWin (2 for BestOf3, 3 for BestOf5).
///   - Acceptance criterion "real-time": every successful point broadcasts NotifyScoreboardUpdatedAsync.
///   - Acceptance criterion "match state": the game transitions to Ended after the final set;
///     MatchEnded is published exactly once.
///   - 404 match / 403 non-organizer / 404 scoreboard / 404 team-not-in-scoreboard.
///   - 409 when not InProgress / 409 when the set is not the current set / 409 when the set is finished.
/// </summary>
public sealed class RecordPointHandlerTests
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

    private RecordPointHandler CreateSut() =>
        new(_matchReader, _scoreboardRepository, _eventPublisher, _notifier, _timeProvider,
            NullLogger<RecordPointHandler>.Instance);

    private void SetupMatch(Guid matchId, Guid organizerId, string status = "Closed") =>
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummary(matchId, organizerId, status));

    /// <summary>Builds an in-progress scoreboard (set 1 open) and wires the repository substitute
    /// to return that same, in-place-mutated instance on every lookup.</summary>
    private Scoreboard SetupInProgress(Guid matchId, ScoreboardFormat format = ScoreboardFormat.BestOf3)
    {
        var scoreboard = Scoreboard.Create(matchId, format, TeamA, TeamB, FixedNow);
        scoreboard.Start(FixedNow);
        scoreboard.OpenSet(setNumber: 1, isDecidingSet: false, FixedNow);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(scoreboard);
        SetupMatch(matchId, Organizer);
        return scoreboard;
    }

    /// <summary>Records <paramref name="count"/> points for <paramref name="teamId"/> on the current set.</summary>
    private async Task<ScoreboardResponse> RecordAsync(
        Guid matchId, Scoreboard scoreboard, Guid teamId, int count)
    {
        ScoreboardResponse response = null!;
        var sut = CreateSut();
        for (var i = 0; i < count; i++)
        {
            response = await sut.HandleAsync(
                matchId, scoreboard.CurrentSetNumber, teamId, Organizer, CancellationToken.None);
        }
        return response;
    }

    // ─── error paths ─────────────────────────────────────────────────────────

    /// <summary>Covers: F1.4 — 404 when match not found.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotFound_when_match_missing()
    {
        var matchId = Guid.NewGuid();
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, 1, TeamA, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    /// <summary>Covers: F1.4 — 403 when caller is not the organizer.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchAccessDenied_when_caller_is_not_organizer()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);

        var act = async () => await CreateSut().HandleAsync(matchId, 1, TeamA, Other, CancellationToken.None);
        await act.Should().ThrowAsync<MatchAccessDeniedException>();
    }

    /// <summary>Covers: F1.4 — 404 when no scoreboard exists.</summary>
    [Fact]
    public async Task HandleAsync_throws_ScoreboardNotFound_when_scoreboard_missing()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, 1, TeamA, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<ScoreboardNotFoundException>();
    }

    /// <summary>Covers: F1.4 — 409 when the scoreboard is not InProgress (e.g. NotStarted).</summary>
    [Fact]
    public async Task HandleAsync_throws_InvalidState_when_not_in_progress()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        var scoreboard = Scoreboard.Create(matchId, ScoreboardFormat.BestOf3, TeamA, TeamB, FixedNow); // NotStarted
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(scoreboard);

        var act = async () => await CreateSut().HandleAsync(matchId, 1, TeamA, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidScoreboardStateException>()
            .WithMessage("*in progress*");
    }

    /// <summary>Covers: F1.4 — 404 when the scoring team is not part of the scoreboard.</summary>
    [Fact]
    public async Task HandleAsync_throws_TeamNotInScoreboard_for_unknown_team()
    {
        var matchId = Guid.NewGuid();
        SetupInProgress(matchId);

        var act = async () => await CreateSut().HandleAsync(
            matchId, 1, Guid.NewGuid(), Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<TeamNotInScoreboardException>();
    }

    /// <summary>Covers: F1.4 — 409 when the target set is not the current set.</summary>
    [Fact]
    public async Task HandleAsync_throws_SetNotCurrent_for_wrong_set_number()
    {
        var matchId = Guid.NewGuid();
        SetupInProgress(matchId); // current set is 1

        var act = async () => await CreateSut().HandleAsync(matchId, 2, TeamA, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<SetNotCurrentException>();
    }

    /// <summary>
    /// Covers: F1.4 — 409 when the current set is already finished (defensive guard).
    /// The set is finished directly while the game remains InProgress to isolate the branch.
    /// </summary>
    [Fact]
    public async Task HandleAsync_throws_InvalidState_when_current_set_finished()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId);
        scoreboard.Sets.First(s => s.SetNumber == 1).Finish(TeamA, FixedNow);

        var act = async () => await CreateSut().HandleAsync(matchId, 1, TeamA, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidScoreboardStateException>()
            .WithMessage("*already finished*");
    }

    // ─── point increment + broadcast ─────────────────────────────────────────

    /// <summary>Covers: F1.4 "set-by-set scoreboard" — a single point increments the scoring team's total.</summary>
    [Fact]
    public async Task HandleAsync_increments_current_set_point()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId);

        var response = await CreateSut().HandleAsync(matchId, 1, TeamA, Organizer, CancellationToken.None);

        response.Sets.Single().TeamAPoints.Should().Be(1);
        response.Sets.Single().TeamBPoints.Should().Be(0);
        response.State.Should().Be("InProgress");
    }

    /// <summary>Covers: F1.4 "real-time" — every recorded point broadcasts NotifyScoreboardUpdatedAsync.</summary>
    [Fact]
    public async Task HandleAsync_broadcasts_on_each_point()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId);

        await RecordAsync(matchId, scoreboard, TeamA, 3);

        await _notifier.Received(3).NotifyScoreboardUpdatedAsync(
            Arg.Any<ScoreboardUpdatedMessage>(), Arg.Any<CancellationToken>());
    }

    // ─── set auto-finish (25, win-by-2, no cap) ──────────────────────────────

    /// <summary>Covers: F1.4 — a regular set auto-finishes at 25 with a 2-point margin (25-0 shutout).</summary>
    [Fact]
    public async Task HandleAsync_auto_finishes_set_at_25()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId);

        await RecordAsync(matchId, scoreboard, TeamA, 25);

        var set1 = scoreboard.Sets.First(s => s.SetNumber == 1);
        set1.Status.Should().Be(SetStatus.Finished);
        set1.WinnerTeamId.Should().Be(TeamA);
        scoreboard.TeamASetsWon.Should().Be(1);
        scoreboard.CurrentSetNumber.Should().Be(2, "a new set opens when the game is not yet decided");
        scoreboard.Sets.Should().HaveCount(2);
    }

    /// <summary>
    /// Covers: F1.4 — win-by-2 with no cap: at 25-24 the set is NOT finished; the extra points that
    /// establish a 2-point margin (26-24) finish the set.
    /// </summary>
    [Fact]
    public async Task HandleAsync_requires_two_point_margin_to_finish_set()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId);

        // A to 24, B to 24 → 24-24
        await RecordAsync(matchId, scoreboard, TeamA, 24);
        await RecordAsync(matchId, scoreboard, TeamB, 24);
        // A scores → 25-24, still in progress (margin 1)
        await CreateSut().HandleAsync(matchId, 1, TeamA, Organizer, CancellationToken.None);

        scoreboard.Sets.First(s => s.SetNumber == 1).Status.Should().Be(SetStatus.InProgress);
        scoreboard.CurrentSetNumber.Should().Be(1);

        // A scores again → 26-24, margin 2 → finished
        await CreateSut().HandleAsync(matchId, 1, TeamA, Organizer, CancellationToken.None);

        var set1 = scoreboard.Sets.First(s => s.SetNumber == 1);
        set1.Status.Should().Be(SetStatus.Finished);
        set1.TeamAPoints.Should().Be(26);
        set1.TeamBPoints.Should().Be(24);
        set1.WinnerTeamId.Should().Be(TeamA);
    }

    // ─── game end (BestOf3) ──────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.4 "set-by-set scoreboard" + "match state" — BestOf3 ends when a team wins 2 sets;
    /// state becomes Ended with the correct winner; MatchEnded is published exactly once.
    /// </summary>
    [Fact]
    public async Task HandleAsync_ends_BestOf3_game_after_two_sets_won()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId, ScoreboardFormat.BestOf3);

        await RecordAsync(matchId, scoreboard, TeamA, 25); // set 1 → A
        var final = await RecordAsync(matchId, scoreboard, TeamA, 25); // set 2 → A → game over

        scoreboard.State.Should().Be(ScoreboardState.Ended);
        scoreboard.WinnerTeamId.Should().Be(TeamA);
        scoreboard.TeamASetsWon.Should().Be(2);
        final.State.Should().Be("Ended");
        final.WinnerTeamId.Should().Be(TeamA);

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchEnded>(e =>
                e.MatchId == matchId && e.WinnerTeamId == TeamA && e.TeamASetsWon == 2 && e.Sets.Count == 2),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: F1.4 — no MatchEnded is published while the game is still in progress.</summary>
    [Fact]
    public async Task HandleAsync_does_not_publish_MatchEnded_before_game_ends()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId, ScoreboardFormat.BestOf3);

        await RecordAsync(matchId, scoreboard, TeamA, 25); // only set 1 won

        await _eventPublisher.DidNotReceive().PublishAsync(
            Arg.Any<MatchEnded>(), Arg.Any<CancellationToken>());
        scoreboard.State.Should().Be(ScoreboardState.InProgress);
    }

    // ─── deciding set target 15 (BestOf3) ────────────────────────────────────

    /// <summary>
    /// Covers: F1.4 — the deciding set (set 3 in BestOf3) targets 15 points, and winning it ends the game.
    /// Sequence: A wins set 1, B wins set 2 (1-1), set 3 is the deciding set (target 15).
    /// </summary>
    [Fact]
    public async Task HandleAsync_deciding_set_targets_15_and_decides_game()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId, ScoreboardFormat.BestOf3);

        await RecordAsync(matchId, scoreboard, TeamA, 25); // set 1 → A
        await RecordAsync(matchId, scoreboard, TeamB, 25); // set 2 → B, 1-1

        scoreboard.CurrentSetNumber.Should().Be(3);
        var decider = scoreboard.Sets.First(s => s.SetNumber == 3);
        decider.IsDecidingSet.Should().BeTrue();

        // 14-0 is not enough (target 15); the 15th point ends the game.
        await RecordAsync(matchId, scoreboard, TeamA, 14);
        decider.Status.Should().Be(SetStatus.InProgress);
        scoreboard.State.Should().Be(ScoreboardState.InProgress);

        var final = await RecordAsync(matchId, scoreboard, TeamA, 1); // 15-0 → deciding set + game won
        decider.Status.Should().Be(SetStatus.Finished);
        decider.TeamAPoints.Should().Be(15);
        scoreboard.State.Should().Be(ScoreboardState.Ended);
        final.WinnerTeamId.Should().Be(TeamA);
    }

    // ─── game end (BestOf5) ──────────────────────────────────────────────────

    /// <summary>Covers: F1.4 — BestOf5 ends only when a team wins 3 sets.</summary>
    [Fact]
    public async Task HandleAsync_ends_BestOf5_game_after_three_sets_won()
    {
        var matchId = Guid.NewGuid();
        var scoreboard = SetupInProgress(matchId, ScoreboardFormat.BestOf5);

        await RecordAsync(matchId, scoreboard, TeamA, 25); // set 1 → A
        await RecordAsync(matchId, scoreboard, TeamB, 25); // set 2 → B
        await RecordAsync(matchId, scoreboard, TeamA, 25); // set 3 → A
        scoreboard.State.Should().Be(ScoreboardState.InProgress, "2-1 is not enough in BestOf5");

        await RecordAsync(matchId, scoreboard, TeamA, 25); // set 4 → A → 3 sets → game over

        scoreboard.State.Should().Be(ScoreboardState.Ended);
        scoreboard.TeamASetsWon.Should().Be(3);
        scoreboard.WinnerTeamId.Should().Be(TeamA);
    }
}
