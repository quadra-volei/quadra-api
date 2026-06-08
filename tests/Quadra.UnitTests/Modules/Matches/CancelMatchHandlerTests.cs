using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>
/// Unit tests for <see cref="CancelMatchHandler"/>.
/// Covers: AC-12 (cancel succeeds), AC-13 (access denied), AC-14 (InProgress → exception), AC-15 (event published).
/// </summary>
public sealed class CancelMatchHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizerId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherUserId = new Guid("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IMatchRepository _repository = Substitute.For<IMatchRepository>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private CancelMatchHandler CreateSut() =>
        new(_repository, _eventPublisher, _timeProvider);

    /// <summary>
    /// Creates a Match in the given status via the public Create factory, then forces a status
    /// override via reflection so we can test non-Draft states without full handler chains.
    /// </summary>
    private static Match BuildMatch(Guid organizerId, MatchStatus status = MatchStatus.Draft)
    {
        var match = Match.Create(
            organizerId: organizerId,
            name: "Test Match",
            description: null,
            address: "Test Address",
            latitude: 0,
            longitude: 0,
            dateTime: FixedNow.AddDays(7),
            maxPlayers: 10,
            regularSlots: 8,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: FixedNow.AddDays(1),
            windowClosesAt: FixedNow.AddDays(6),
            now: FixedNow);

        if (status != MatchStatus.Draft)
        {
            typeof(Match).GetProperty(nameof(Match.Status))!.SetValue(match, status);
        }

        return match;
    }

    // ─── AC-12: cancel succeeds ───────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-12 — organizer can cancel a Draft match; status becomes Cancelled.
    /// </summary>
    [Fact]
    public async Task HandleAsync_organizer_can_cancel_Draft_match()
    {
        var match = BuildMatch(OrganizerId, MatchStatus.Draft);
        _repository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);

        var sut = CreateSut();
        await sut.HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        match.Status.Should().Be(MatchStatus.Cancelled);
        await _repository.Received(1).UpdateAsync(match, Arg.Any<CancellationToken>());
    }

    // ─── AC-13: access denied ────────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-13 — non-organizer attempting to cancel throws MatchAccessDeniedException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_non_organizer_throws_MatchAccessDeniedException()
    {
        var match = BuildMatch(OrganizerId, MatchStatus.Draft);
        _repository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(match.Id, OtherUserId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchAccessDeniedException>();
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<Match>(), Arg.Any<CancellationToken>());
        await _eventPublisher.DidNotReceive().PublishAsync(Arg.Any<MatchStatusChanged>(), Arg.Any<CancellationToken>());
    }

    // ─── AC-14: InProgress → 409 ─────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-14 — cancelling an InProgress match throws InvalidMatchStatusTransitionException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_InProgress_match_throws_InvalidMatchStatusTransitionException()
    {
        var match = BuildMatch(OrganizerId, MatchStatus.InProgress);
        _repository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMatchStatusTransitionException>();
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<Match>(), Arg.Any<CancellationToken>());
        await _eventPublisher.DidNotReceive().PublishAsync(Arg.Any<MatchStatusChanged>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: cancelling an Ended match also throws InvalidMatchStatusTransitionException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Ended_match_throws_InvalidMatchStatusTransitionException()
    {
        var match = BuildMatch(OrganizerId, MatchStatus.Ended);
        _repository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMatchStatusTransitionException>();
    }

    // ─── AC-15: MatchStatusChanged event ─────────────────────────────────────

    /// <summary>
    /// Covers: AC-15 — MatchStatusChanged event is published after successful cancellation.
    /// </summary>
    [Fact]
    public async Task HandleAsync_publishes_MatchStatusChanged_AFTER_UpdateAsync()
    {
        var match = BuildMatch(OrganizerId, MatchStatus.Draft);
        _repository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);

        var sut = CreateSut();

        var updateCalled = false;
        var publishCalledBeforeUpdate = false;

        _repository.UpdateAsync(Arg.Any<Match>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                updateCalled = true;
                return Task.CompletedTask;
            });

        _eventPublisher.PublishAsync(Arg.Any<MatchStatusChanged>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                if (!updateCalled)
                {
                    publishCalledBeforeUpdate = true;
                }

                return Task.CompletedTask;
            });

        await sut.HandleAsync(match.Id, OrganizerId, CancellationToken.None);

        publishCalledBeforeUpdate.Should().BeFalse("MatchStatusChanged must only be published AFTER UpdateAsync succeeds");

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchStatusChanged>(e =>
                e.MatchId == match.Id
                && e.OrganizerId == OrganizerId
                && e.PreviousStatus == "Draft"
                && e.NewStatus == "Cancelled"
                && e.OccurredAt == FixedNow),
            Arg.Any<CancellationToken>());
    }

    // ─── 404 ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Covers: handler throws MatchNotFoundException when match does not exist.
    /// </summary>
    [Fact]
    public async Task HandleAsync_non_existent_match_throws_MatchNotFoundException()
    {
        var matchId = Guid.NewGuid();
        _repository.FindByIdAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(matchId, OrganizerId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
