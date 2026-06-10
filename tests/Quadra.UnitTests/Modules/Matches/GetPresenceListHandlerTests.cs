using FluentAssertions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>
/// Unit tests for <see cref="GetPresenceListHandler"/>.
/// Covers: F1.2 — GET /api/v1/matches/{id}/presences returns presences partitioned into
/// Confirmed, Pending, Declined lists plus a WaitingList.
/// </summary>
public sealed class GetPresenceListHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizerId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();
    private readonly IPresenceRepository _presenceRepository = Substitute.For<IPresenceRepository>();
    private readonly IWaitingListRepository _waitingListRepository = Substitute.For<IWaitingListRepository>();

    private GetPresenceListHandler CreateSut() =>
        new(_matchRepository, _presenceRepository, _waitingListRepository);

    private static Match BuildMatch(int regularSlots = 10)
    {
        return Match.Create(
            organizerId: OrganizerId,
            name: "Sunday Volleyball",
            description: null,
            address: "Rua Teste, 123",
            latitude: -23.5505,
            longitude: -46.6333,
            dateTime: FixedNow.AddDays(7),
            maxPlayers: regularSlots + 2,
            regularSlots: regularSlots,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: FixedNow.AddDays(1),
            windowClosesAt: FixedNow.AddDays(6),
            now: FixedNow);
    }

    // ─── AC: presence list is partitioned into Confirmed/Pending/Declined + WaitingList ──

    /// <summary>
    /// Covers: F1.2 — GET /api/v1/matches/{id}/presences returns presences partitioned into
    /// Confirmed, Pending, Declined lists plus a WaitingList.
    /// </summary>
    [Fact]
    public async Task HandleAsync_returns_presences_partitioned_into_all_three_buckets_plus_waiting_list()
    {
        var match = BuildMatch();
        var matchId = match.Id;

        var confirmedPlayer = Guid.NewGuid();
        var pendingPlayer = Guid.NewGuid();
        var declinedPlayer = Guid.NewGuid();
        var waitingPlayer = Guid.NewGuid();

        var confirmedPresence = MatchPresence.Create(matchId, confirmedPlayer, PlayerType.Regular, FixedNow);
        confirmedPresence.Confirm(FixedNow);

        var pendingPresence = MatchPresence.Create(matchId, pendingPlayer, PlayerType.Regular, FixedNow);

        var declinedPresence = MatchPresence.Create(matchId, declinedPlayer, PlayerType.Regular, FixedNow);
        declinedPresence.Decline(FixedNow);

        var waitingEntry = WaitingListEntry.Create(matchId, waitingPlayer, PlayerType.Regular, 1, FixedNow);

        _matchRepository.FindByIdAsync(matchId, Arg.Any<CancellationToken>()).Returns(match);

        _presenceRepository.ListByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MatchPresence>)new List<MatchPresence>
            {
                confirmedPresence,
                pendingPresence,
                declinedPresence,
            });

        _waitingListRepository.ListByMatchAsync(matchId, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WaitingListEntry>)new List<WaitingListEntry> { waitingEntry });

        var sut = CreateSut();
        var result = await sut.HandleAsync(matchId, CancellationToken.None);

        result.Should().NotBeNull();

        result.Confirmed.Should().HaveCount(1);
        result.Confirmed[0].PlayerId.Should().Be(confirmedPlayer);
        result.Confirmed[0].Status.Should().Be("Confirmed");

        result.Pending.Should().HaveCount(1);
        result.Pending[0].PlayerId.Should().Be(pendingPlayer);
        result.Pending[0].Status.Should().Be("Pending");

        result.Declined.Should().HaveCount(1);
        result.Declined[0].PlayerId.Should().Be(declinedPlayer);
        result.Declined[0].Status.Should().Be("Declined");

        result.WaitingList.Should().HaveCount(1);
        result.WaitingList[0].PlayerId.Should().Be(waitingPlayer);
        result.WaitingList[0].Position.Should().Be(1);
        result.WaitingList[0].PlayerType.Should().Be("Regular");

        result.ConfirmedCount.Should().Be(1);
        result.RegularSlots.Should().Be(match.RegularSlots);
        result.DropInSlots.Should().Be(match.DropInSlots);
    }

    /// <summary>
    /// Covers: GET returns empty lists when no presences exist for a match.
    /// </summary>
    [Fact]
    public async Task HandleAsync_with_no_presences_returns_all_empty_lists()
    {
        var match = BuildMatch();

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.ListByMatchAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MatchPresence>)new List<MatchPresence>());
        _waitingListRepository.ListByMatchAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WaitingListEntry>)new List<WaitingListEntry>());

        var sut = CreateSut();
        var result = await sut.HandleAsync(match.Id, CancellationToken.None);

        result.Confirmed.Should().BeEmpty();
        result.Pending.Should().BeEmpty();
        result.Declined.Should().BeEmpty();
        result.WaitingList.Should().BeEmpty();
        result.ConfirmedCount.Should().Be(0);
    }

    /// <summary>
    /// Covers: GET returns MatchNotFoundException when match does not exist.
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

    /// <summary>
    /// Covers: WaitingList entries are ordered by position ASC (repository contract),
    /// and the response correctly maps position values.
    /// </summary>
    [Fact]
    public async Task HandleAsync_maps_waiting_list_entries_with_correct_position_and_player_type()
    {
        var match = BuildMatch();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();

        var entry1 = WaitingListEntry.Create(match.Id, p1, PlayerType.Regular, 1, FixedNow);
        var entry2 = WaitingListEntry.Create(match.Id, p2, PlayerType.DropIn, 2, FixedNow);

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.ListByMatchAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<MatchPresence>)new List<MatchPresence>());
        _waitingListRepository.ListByMatchAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WaitingListEntry>)new List<WaitingListEntry> { entry1, entry2 });

        var sut = CreateSut();
        var result = await sut.HandleAsync(match.Id, CancellationToken.None);

        result.WaitingList.Should().HaveCount(2);
        result.WaitingList[0].PlayerId.Should().Be(p1);
        result.WaitingList[0].Position.Should().Be(1);
        result.WaitingList[0].PlayerType.Should().Be("Regular");

        result.WaitingList[1].PlayerId.Should().Be(p2);
        result.WaitingList[1].Position.Should().Be(2);
        result.WaitingList[1].PlayerType.Should().Be("DropIn");
    }
}
