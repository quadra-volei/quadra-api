using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
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
/// Unit tests for <see cref="OpenMatchWindowHandler"/>.
/// Covers: AC — OpenMatchWindowHandler publishes MatchWindowOpened SQS event after transitioning Draft → Open.
/// </summary>
public sealed class OpenMatchWindowHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizerId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();
    private readonly IPresenceRepository _presenceRepository = Substitute.For<IPresenceRepository>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private OpenMatchWindowHandler CreateSut() =>
        new(_matchRepository, _presenceRepository, _eventPublisher, _timeProvider,
            NullLogger<OpenMatchWindowHandler>.Instance);

    private static Match BuildDraftMatch(MatchStatus? overrideStatus = null)
    {
        var match = Match.Create(
            organizerId: OrganizerId,
            name: "Sunday Volleyball",
            description: null,
            address: "Rua Teste, 123",
            latitude: -23.5505,
            longitude: -46.6333,
            dateTime: FixedNow.AddDays(7),
            maxPlayers: 12,
            regularSlots: 10,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: FixedNow.AddDays(1),
            windowClosesAt: FixedNow.AddDays(6),
            now: FixedNow);

        if (overrideStatus.HasValue)
        {
            typeof(Match).GetProperty(nameof(Match.Status))!.SetValue(match, overrideStatus.Value);
        }

        return match;
    }

    // ─── AC: publishes MatchWindowOpened after Draft → Open ─────────────────

    /// <summary>
    /// Covers: F1.2 — OpenMatchWindowHandler publishes MatchWindowOpened SQS event after
    /// transitioning a match from Draft to Open.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Draft_match_transitions_to_Open_and_publishes_MatchWindowOpened()
    {
        var match = BuildDraftMatch();
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.ListByMatchAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MatchPresence>());

        var sut = CreateSut();
        await sut.HandleAsync(match.Id, CancellationToken.None);

        // Status must transition to Open.
        match.Status.Should().Be(MatchStatus.Open);

        // Repository must be updated.
        await _matchRepository.Received(1).UpdateAsync(match, Arg.Any<CancellationToken>());

        // MatchWindowOpened event must be published.
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchWindowOpened>(e =>
                e.MatchId == match.Id
                && e.OrganizerId == OrganizerId
                && e.WindowClosesAt == match.WindowClosesAt
                && e.OccurredAt == FixedNow),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: MatchWindowOpened includes all pending player IDs from the presence list.
    /// </summary>
    [Fact]
    public async Task HandleAsync_includes_pending_player_ids_in_event()
    {
        var match = BuildDraftMatch();
        var pendingPlayer1 = Guid.NewGuid();
        var pendingPlayer2 = Guid.NewGuid();
        var confirmedPlayer = Guid.NewGuid();

        var presences = new List<MatchPresence>
        {
            MatchPresence.Create(match.Id, pendingPlayer1, PlayerType.Regular, FixedNow),
            MatchPresence.Create(match.Id, pendingPlayer2, PlayerType.Regular, FixedNow),
        };

        // Simulate a confirmed player — Confirm() mutates the object.
        var confirmedPresence = MatchPresence.Create(match.Id, confirmedPlayer, PlayerType.Regular, FixedNow);
        confirmedPresence.Confirm(FixedNow);
        presences.Add(confirmedPresence);

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.ListByMatchAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MatchPresence>)presences);

        var sut = CreateSut();
        await sut.HandleAsync(match.Id, CancellationToken.None);

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchWindowOpened>(e =>
                e.PendingPlayerIds.Count == 2
                && e.PendingPlayerIds.Contains(pendingPlayer1)
                && e.PendingPlayerIds.Contains(pendingPlayer2)
                && !e.PendingPlayerIds.Contains(confirmedPlayer)),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: event is published only AFTER UpdateAsync succeeds (ordering guarantee).
    /// </summary>
    [Fact]
    public async Task HandleAsync_publishes_event_only_after_UpdateAsync()
    {
        var match = BuildDraftMatch();
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.ListByMatchAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns(new List<MatchPresence>());

        var updateCalled = false;
        var publishCalledBeforeUpdate = false;

        _matchRepository.UpdateAsync(Arg.Any<Match>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                updateCalled = true;
                return Task.CompletedTask;
            });

        _eventPublisher.PublishAsync(Arg.Any<MatchWindowOpened>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                if (!updateCalled) publishCalledBeforeUpdate = true;
                return Task.CompletedTask;
            });

        var sut = CreateSut();
        await sut.HandleAsync(match.Id, CancellationToken.None);

        publishCalledBeforeUpdate.Should().BeFalse(
            "MatchWindowOpened must only be published AFTER UpdateAsync succeeds");
    }

    /// <summary>
    /// Covers: HandleAsync throws InvalidMatchStatusTransitionException when match is not Draft.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Open_match_throws_InvalidMatchStatusTransitionException()
    {
        var match = BuildDraftMatch(MatchStatus.Open);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(match.Id, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMatchStatusTransitionException>();
        await _eventPublisher.DidNotReceive().PublishAsync(
            Arg.Any<MatchWindowOpened>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: HandleAsync throws MatchNotFoundException when match does not exist.
    /// </summary>
    [Fact]
    public async Task HandleAsync_non_existent_match_throws_MatchNotFoundException()
    {
        var matchId = Guid.NewGuid();
        _matchRepository.FindByIdAsync(matchId, Arg.Any<CancellationToken>()).ReturnsNull();

        var sut = CreateSut();

        var act = async () => await sut.HandleAsync(matchId, CancellationToken.None);

        await act.Should().ThrowAsync<MatchNotFoundException>();
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
