using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Modules.InGame.Application;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="OpenMvpVotingHandler"/>.
///
/// Covers (F1.5):
///   - AC-3 "voting deadline (set by Organizer; default 24h)": persists the request deadline when
///     provided; defaults to opened_at + 24h when omitted.
///   - "open requires organizer" (403) and "open requires ended scoreboard" (409).
///   - 404 match not found; 409 duplicate voting session (both eager check and race via DbUpdateException).
/// </summary>
public sealed class OpenMvpVotingHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizer = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Other = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid TeamA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid TeamB = new("22222222-2222-2222-2222-222222222222");

    private readonly IMatchReader _matchReader = Substitute.For<IMatchReader>();
    private readonly IScoreboardRepository _scoreboardRepository = Substitute.For<IScoreboardRepository>();
    private readonly IMvpVotingRepository _votingRepository = Substitute.For<IMvpVotingRepository>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private OpenMvpVotingHandler CreateSut() =>
        new(_matchReader, _scoreboardRepository, _votingRepository, _timeProvider,
            NullLogger<OpenMvpVotingHandler>.Instance);

    private void SetupMatch(Guid matchId, Guid organizerId) =>
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummary(matchId, organizerId, "Closed"));

    private void SetupEndedScoreboard(Guid matchId)
    {
        var scoreboard = Scoreboard.Create(matchId, ScoreboardFormat.BestOf3, TeamA, TeamB, FixedNow);
        scoreboard.Start(FixedNow);
        scoreboard.End(TeamA, FixedNow);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(scoreboard);
    }

    // ─── error paths ─────────────────────────────────────────────────────────

    /// <summary>Covers: F1.5 — 404 when the match does not exist.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotFound_when_match_missing()
    {
        var matchId = Guid.NewGuid();
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, null, CancellationToken.None);
        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    /// <summary>Covers: F1.5 "open requires organizer" — 403 when the caller is not the organizer.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchAccessDenied_when_caller_is_not_organizer()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);

        var act = async () => await CreateSut().HandleAsync(matchId, Other, null, CancellationToken.None);
        await act.Should().ThrowAsync<MatchAccessDeniedException>();
    }

    /// <summary>Covers: F1.5 "open requires ended scoreboard" — 409 when no scoreboard exists.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotEnded_when_scoreboard_missing()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, null, CancellationToken.None);
        await act.Should().ThrowAsync<MatchNotEndedException>();
    }

    /// <summary>Covers: F1.5 "open requires ended scoreboard" — 409 when the scoreboard is not Ended.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotEnded_when_scoreboard_not_ended()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        var scoreboard = Scoreboard.Create(matchId, ScoreboardFormat.BestOf3, TeamA, TeamB, FixedNow);
        scoreboard.Start(FixedNow); // InProgress
        _scoreboardRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(scoreboard);

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, null, CancellationToken.None);
        await act.Should().ThrowAsync<MatchNotEndedException>();
    }

    /// <summary>Covers: F1.5 — 409 when a voting session already exists (eager check).</summary>
    [Fact]
    public async Task HandleAsync_throws_AlreadyExists_when_voting_present()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        SetupEndedScoreboard(matchId);
        _votingRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(MvpVoting.Open(matchId, FixedNow.AddHours(24), FixedNow));

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, null, CancellationToken.None);
        await act.Should().ThrowAsync<MvpVotingAlreadyExistsException>();
    }

    /// <summary>Covers: F1.5 — 409 when a concurrent open races the UNIQUE (match_id) constraint.</summary>
    [Fact]
    public async Task HandleAsync_maps_DbUpdateException_to_AlreadyExists()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        SetupEndedScoreboard(matchId);
        _votingRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();
        _votingRepository.AddAsync(Arg.Any<MvpVoting>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new DbUpdateException("duplicate"));

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, null, CancellationToken.None);
        await act.Should().ThrowAsync<MvpVotingAlreadyExistsException>();
    }

    // ─── deadline behaviour (AC-3) ───────────────────────────────────────────

    /// <summary>
    /// Covers: F1.5 AC-3 — when the request omits a deadline, it defaults to opened_at + 24h.
    /// </summary>
    [Fact]
    public async Task HandleAsync_defaults_deadline_to_opened_at_plus_24h_when_omitted()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        SetupEndedScoreboard(matchId);
        _votingRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var response = await CreateSut().HandleAsync(matchId, Organizer, null, CancellationToken.None);

        response.DeadlineAt.Should().Be(FixedNow.AddHours(24));
        response.OpenedAt.Should().Be(FixedNow);
        response.DeadlineAt.Should().Be(response.OpenedAt.AddHours(24));
    }

    /// <summary>Covers: F1.5 AC-3 — the request's deadline is persisted verbatim when provided.</summary>
    [Fact]
    public async Task HandleAsync_persists_provided_deadline()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        SetupEndedScoreboard(matchId);
        _votingRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();
        var requestedDeadline = FixedNow.AddHours(6);

        var response = await CreateSut().HandleAsync(matchId, Organizer, requestedDeadline, CancellationToken.None);

        response.DeadlineAt.Should().Be(requestedDeadline);
        await _votingRepository.Received(1).AddAsync(
            Arg.Is<MvpVoting>(v => v.MatchId == matchId && v.DeadlineAt == requestedDeadline),
            Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: F1.5 — a freshly opened session is Open with zero votes and no MVP.</summary>
    [Fact]
    public async Task HandleAsync_opens_session_in_Open_state_with_zero_votes()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        SetupEndedScoreboard(matchId);
        _votingRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var response = await CreateSut().HandleAsync(matchId, Organizer, null, CancellationToken.None);

        response.State.Should().Be("Open");
        response.TotalVotes.Should().Be(0);
        response.MvpPlayerId.Should().BeNull();
        response.CallerHasVoted.Should().BeFalse();
        response.Results.Should().BeEmpty();
        await _votingRepository.Received(1).AddAsync(Arg.Any<MvpVoting>(), Arg.Any<CancellationToken>());
    }
}
