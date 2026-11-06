using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;
using Quadra.Shared.Realtime;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>
/// The organizer takes a player off the match before the game starts; the freed slot goes to
/// the first one waiting. Nobody else can remove anyone.
/// </summary>
public sealed class RemovePresenceHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizerId = Guid.NewGuid();

    private readonly IMatchRepository _matches = Substitute.For<IMatchRepository>();
    private readonly IPresenceRepository _presences = Substitute.For<IPresenceRepository>();
    private readonly IWaitingListRepository _waitingList = Substitute.For<IWaitingListRepository>();
    private readonly IEventPublisher _events = Substitute.For<IEventPublisher>();

    private RemovePresenceHandler CreateSut() => new(
        _matches, _presences, _waitingList, _events, Substitute.For<IMatchRoomNotifier>(),
        new FakeTime(Now), NullLogger<RemovePresenceHandler>.Instance);

    private Match OpenMatch(MatchStatus status = MatchStatus.Open)
    {
        var match = MatchTestSupport.Match(OrganizerId, Now, Now.AddHours(-1), Now.AddHours(1), status);
        _matches.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        return match;
    }

    private MatchPresence Confirmed(Guid matchId, Guid playerId)
    {
        var presence = MatchPresence.Create(matchId, playerId, PlayerType.Regular, Now);
        presence.Confirm(Now);
        _presences.FindByMatchAndPlayerAsync(matchId, playerId, Arg.Any<CancellationToken>()).Returns(presence);
        return presence;
    }

    [Fact]
    public async Task A_player_who_is_not_the_organizer_cannot_remove_anyone()
    {
        var match = OpenMatch();
        var player = Guid.NewGuid();
        Confirmed(match.Id, player);

        var act = () => CreateSut().HandleAsync(match.Id, Guid.NewGuid(), player, CancellationToken.None);

        await act.Should().ThrowAsync<MatchAccessDeniedException>();
        await _presences.DidNotReceive().DeleteAsync(Arg.Any<MatchPresence>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nobody_is_removed_once_the_game_started()
    {
        var match = OpenMatch(MatchStatus.InProgress);
        var player = Guid.NewGuid();
        Confirmed(match.Id, player);

        var act = () => CreateSut().HandleAsync(match.Id, OrganizerId, player, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidMatchStatusTransitionException>();
    }

    [Fact]
    public async Task The_organizer_removes_a_confirmed_player_and_the_first_waiting_takes_the_slot()
    {
        var match = OpenMatch();
        var player = Guid.NewGuid();
        var presence = Confirmed(match.Id, player);
        var waiter = WaitingListEntry.Create(match.Id, Guid.NewGuid(), PlayerType.Regular, 1, Now);
        _waitingList.ListByMatchAsync(match.Id, Arg.Any<CancellationToken>()).Returns([waiter]);

        await CreateSut().HandleAsync(match.Id, OrganizerId, player, CancellationToken.None);

        await _presences.Received(1).DeleteAsync(presence, Arg.Any<CancellationToken>());
        await _presences.Received(1).AddAsync(
            Arg.Is<MatchPresence>(p => p.PlayerId == waiter.PlayerId), Arg.Any<CancellationToken>());
        await _events.Received(1).PublishAsync(
            Arg.Is<WaitingListPromoted>(e => e.PlayerId == waiter.PlayerId), Arg.Any<CancellationToken>());
    }

    private sealed class FakeTime(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
