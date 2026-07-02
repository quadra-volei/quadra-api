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

namespace Quadra.UnitTests.Modules.InGame;

/// <summary>
/// Unit tests for <see cref="CloseMvpVotingHandler"/>.
///
/// Covers (F1.5 AC-2 "automatic MVP selection from votes"):
///   - with zero votes the session closes with MvpPlayerId null and publishes NO MvpAwarded event.
/// Covers "close requires organizer" (403) and 404 match / 404 voting / 409 already closed.
///
/// The "with votes → publishes MvpAwarded" path requires votes attached to the aggregate (an
/// InGame-internal operation) and is exercised end-to-end in the integration suite; the pure
/// winner-selection is covered by <see cref="MvpSelectionRulesTests"/>.
/// </summary>
public sealed class CloseMvpVotingHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Organizer = new("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid Other = new("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IMatchReader _matchReader = Substitute.For<IMatchReader>();
    private readonly IMvpVotingRepository _votingRepository = Substitute.For<IMvpVotingRepository>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private CloseMvpVotingHandler CreateSut() =>
        new(_matchReader, _votingRepository, _eventPublisher, _timeProvider,
            NullLogger<CloseMvpVotingHandler>.Instance);

    private void SetupMatch(Guid matchId, Guid organizerId) =>
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>())
            .Returns(new MatchSummary(matchId, organizerId, "Closed"));

    // ─── error paths ─────────────────────────────────────────────────────────

    /// <summary>Covers: F1.5 — 404 when the match does not exist.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchNotFound_when_match_missing()
    {
        var matchId = Guid.NewGuid();
        _matchReader.FindMatchSummaryAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    /// <summary>Covers: F1.5 "close requires organizer" — 403 when the caller is not the organizer.</summary>
    [Fact]
    public async Task HandleAsync_throws_MatchAccessDenied_when_caller_is_not_organizer()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);

        var act = async () => await CreateSut().HandleAsync(matchId, Other, CancellationToken.None);
        await act.Should().ThrowAsync<MatchAccessDeniedException>();
    }

    /// <summary>Covers: F1.5 — 404 when no voting session has been opened.</summary>
    [Fact]
    public async Task HandleAsync_throws_MvpVotingNotFound_when_voting_missing()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        _votingRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<MvpVotingNotFoundException>();
    }

    /// <summary>Covers: F1.5 — 409 when the voting session is already closed.</summary>
    [Fact]
    public async Task HandleAsync_throws_InvalidState_when_already_closed()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        var voting = MvpVoting.Open(matchId, FixedNow.AddHours(24), FixedNow);
        voting.Close(null, null, FixedNow);
        _votingRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(voting);

        var act = async () => await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);
        await act.Should().ThrowAsync<InvalidMvpVotingStateException>().WithMessage("*already closed*");
    }

    // ─── zero votes (AC-2) ────────────────────────────────────────────────────

    /// <summary>
    /// Covers: F1.5 AC-2 — closing a session with zero votes sets MvpPlayerId null, persists the
    /// close, and publishes NO MvpAwarded event.
    /// </summary>
    [Fact]
    public async Task HandleAsync_with_zero_votes_closes_with_null_mvp_and_publishes_nothing()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        var voting = MvpVoting.Open(matchId, FixedNow.AddHours(24), FixedNow); // no votes attached
        _votingRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(voting);

        var response = await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);

        response.State.Should().Be("Closed");
        response.MvpPlayerId.Should().BeNull();
        response.MvpVoteCount.Should().BeNull();
        voting.State.Should().Be(MvpVotingState.Closed);
        voting.ClosedAt.Should().Be(FixedNow);

        await _votingRepository.Received(1).UpdateAsync(voting, Arg.Any<CancellationToken>());
        await _eventPublisher.DidNotReceive().PublishAsync(
            Arg.Any<MvpAwarded>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Covers: F1.5 — a session with zero votes still transitions to Closed and is persisted.</summary>
    [Fact]
    public async Task HandleAsync_persists_close_before_returning()
    {
        var matchId = Guid.NewGuid();
        SetupMatch(matchId, Organizer);
        var voting = MvpVoting.Open(matchId, FixedNow.AddHours(24), FixedNow);
        _votingRepository.FindByMatchAsync(matchId, Arg.Any<CancellationToken>()).Returns(voting);

        await CreateSut().HandleAsync(matchId, Organizer, CancellationToken.None);

        await _votingRepository.Received(1).UpdateAsync(
            Arg.Is<MvpVoting>(v => v.State == MvpVotingState.Closed), Arg.Any<CancellationToken>());
    }
}
