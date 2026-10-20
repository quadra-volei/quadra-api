using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ReturnsExtensions;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;
using Quadra.Shared.Realtime;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>
/// Unit tests for <see cref="UpdateMyPresenceHandler"/>.
/// Covers:
///   - AC: waiting list insertion returns 409 with position when slots are full.
///   - AC: DropIn gets HTTP 200 after windowClosesAt when released slots are available.
/// </summary>
public sealed class UpdateMyPresenceHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizerId = new Guid("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid PlayerId = new Guid("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly IMatchRepository _matchRepository = Substitute.For<IMatchRepository>();
    private readonly IPresenceRepository _presenceRepository = Substitute.For<IPresenceRepository>();
    private readonly IWaitingListRepository _waitingListRepository = Substitute.For<IWaitingListRepository>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private readonly IMatchRoomNotifier _roomNotifier = Substitute.For<IMatchRoomNotifier>();
    private readonly IMatchGuestRepository _guestRepository = Substitute.For<IMatchGuestRepository>();

    private UpdateMyPresenceHandler CreateSut(DateTimeOffset now) =>
        new(_matchRepository, _presenceRepository, _waitingListRepository, _guestRepository,
            MatchTestSupport.Synchronizer(
                _matchRepository, new FixedTimeProvider(now), _presenceRepository, _waitingListRepository, _eventPublisher),
            _eventPublisher, _roomNotifier, new FixedTimeProvider(now),
            NullLogger<UpdateMyPresenceHandler>.Instance);

    /// <summary>
    /// Builds an Open match where "now" falls within [WindowOpensAt, WindowClosesAt].
    /// WindowOpensAt = FixedNow - 1h, WindowClosesAt = FixedNow + 1h.
    /// </summary>
    private static Match BuildOpenMatch(
        int regularSlots = 2,
        short? releasedDropInSlots = null)
    {
        var windowOpensAt = FixedNow.AddHours(-1);
        var windowClosesAt = FixedNow.AddHours(1);

        var match = Match.Create(
            organizerId: OrganizerId,
            name: "Sunday Volleyball",
            description: null,
            address: "Rua Teste, 123",
            latitude: -23.5505,
            longitude: -46.6333,
            dateTime: FixedNow.AddDays(7),
            maxPlayers: regularSlots + 2, // some DropIn slots
            regularSlots: regularSlots,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: windowOpensAt,
            windowClosesAt: windowClosesAt,
            now: FixedNow);

        // Force Open status.
        typeof(Match).GetProperty(nameof(Match.Status))!.SetValue(match, MatchStatus.Open);

        if (releasedDropInSlots.HasValue)
        {
            typeof(Match).GetProperty(nameof(Match.ReleasedDropInSlots))!.SetValue(match, releasedDropInSlots);
        }

        return match;
    }

    /// <summary>
    /// Builds a Closed match for DropIn confirmation tests.
    /// windowClosesAt is in the past (FixedNow - 1 min) so the condition
    /// "now > WindowClosesAt AND status == Closed" is satisfied.
    /// </summary>
    private static Match BuildClosedMatchWithReleasedSlots(int releasedSlots = 1, int dropInSlots = 0)
    {
        var windowClosesAt = FixedNow.AddMinutes(-1);

        var match = Match.Create(
            organizerId: OrganizerId,
            name: "Sunday Volleyball",
            description: null,
            address: "Rua Teste, 123",
            latitude: -23.5505,
            longitude: -46.6333,
            dateTime: FixedNow.AddDays(7),
            maxPlayers: 4,  // 2 Regular + 2 DropIn
            regularSlots: 2,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: FixedNow.AddDays(-2),
            windowClosesAt: windowClosesAt,
            now: FixedNow);

        // Force Closed status and ReleasedDropInSlots.
        typeof(Match).GetProperty(nameof(Match.Status))!.SetValue(match, MatchStatus.Closed);
        typeof(Match).GetProperty(nameof(Match.ReleasedDropInSlots))!.SetValue(match, (short)releasedSlots);

        return match;
    }

    // ─── AC: slot full → waiting list inserted, 409 thrown ──────────────────

    /// <summary>
    /// Covers: F1.2 — When a Regular player confirms and all RegularSlots are taken,
    /// a row is inserted in waiting_list and SlotLimitReachedException is thrown with the position.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Regular_confirm_when_slots_full_inserts_waiting_list_entry_and_throws()
    {
        var match = BuildOpenMatch(regularSlots: 2);
        var presence = MatchPresence.Create(match.Id, PlayerId, PlayerType.Regular, FixedNow);

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .Returns(presence);

        // All 2 Regular slots are already confirmed.
        _presenceRepository.CountConfirmedAsync(match.Id, PlayerType.Regular, Arg.Any<CancellationToken>())
            .Returns(2);

        // Player is not already on waiting list.
        _waitingListRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        // Next waiting list position = 1 (first entry).
        _waitingListRepository.GetNextPositionAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns(1);

        var request = new UpdateMyPresenceRequest(Status: "Confirmed");
        var sut = CreateSut(FixedNow);

        var act = async () => await sut.HandleAsync(match.Id, PlayerId, request, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<SlotLimitReachedException>();
        exception.Which.WaitingListPosition.Should().Be(1);
        exception.Which.AlreadyOnWaitingList.Should().BeFalse();

        // Waiting list entry must have been persisted.
        await _waitingListRepository.Received(1).AddAsync(
            Arg.Is<WaitingListEntry>(e =>
                e.MatchId == match.Id
                && e.PlayerId == PlayerId
                && e.PlayerType == PlayerType.Regular
                && e.Position == 1),
            Arg.Any<CancellationToken>());

        // Presence row must NOT have been updated to Confirmed.
        await _presenceRepository.DidNotReceive().UpdateAsync(
            Arg.Any<MatchPresence>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.2 — Player already on waiting list gets AlreadyOnWaitingList=true exception.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Regular_confirm_when_already_on_waiting_list_throws_with_AlreadyOnWaitingList()
    {
        var match = BuildOpenMatch(regularSlots: 2);
        var presence = MatchPresence.Create(match.Id, PlayerId, PlayerType.Regular, FixedNow);
        var existingEntry = WaitingListEntry.Create(match.Id, PlayerId, PlayerType.Regular, 1, FixedNow);

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .Returns(presence);
        _presenceRepository.CountConfirmedAsync(match.Id, PlayerType.Regular, Arg.Any<CancellationToken>())
            .Returns(2); // all slots taken

        _waitingListRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .Returns(existingEntry);

        var request = new UpdateMyPresenceRequest(Status: "Confirmed");
        var sut = CreateSut(FixedNow);

        var act = async () => await sut.HandleAsync(match.Id, PlayerId, request, CancellationToken.None);

        var exception = await act.Should().ThrowAsync<SlotLimitReachedException>();
        exception.Which.AlreadyOnWaitingList.Should().BeTrue();

        // Must NOT add a second entry.
        await _waitingListRepository.DidNotReceive().AddAsync(
            Arg.Any<WaitingListEntry>(), Arg.Any<CancellationToken>());
    }

    // ─── AC: DropIn gets 200 (confirmed) after window closes with released slots ─

    /// <summary>
    /// Covers: F1.2 — After windowClosesAt, a DropIn player who attempts to confirm when all
    /// original DropInSlots are taken but at least one Regular slot was released gets HTTP 200
    /// (successful confirmation) instead of 409.
    ///
    /// This tests that DropInSlots + ReleasedDropInSlots is the effective limit:
    /// - DropInSlots = 2 (computed: maxPlayers 4 - regularSlots 2)
    /// - ReleasedDropInSlots = 1
    /// - Effective limit = 3
    /// - Confirmed DropIns = 2 (less than effective limit → confirmation succeeds)
    /// </summary>
    [Fact]
    public async Task HandleAsync_DropIn_confirm_with_released_slots_available_succeeds_with_Confirmed()
    {
        // Closed match with 1 released Regular slot → effective DropIn limit = 2 original + 1 released = 3.
        var match = BuildClosedMatchWithReleasedSlots(releasedSlots: 1);
        var dropInPlayerId = Guid.NewGuid();
        var presence = MatchPresence.Create(match.Id, dropInPlayerId, PlayerType.DropIn, FixedNow);

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, dropInPlayerId, Arg.Any<CancellationToken>())
            .Returns(presence);

        // 2 DropIn already confirmed; original DropInSlots = 2; but released = 1, so effective = 3.
        // 2 < 3 → confirmation should succeed.
        _presenceRepository.CountConfirmedAsync(match.Id, PlayerType.DropIn, Arg.Any<CancellationToken>())
            .Returns(2);

        _presenceRepository.CountConfirmedAsync(match.Id, PlayerType.Regular, Arg.Any<CancellationToken>())
            .Returns(0); // for the broadcast count after confirm

        var request = new UpdateMyPresenceRequest(Status: "Confirmed");

        // "now" must be strictly after WindowClosesAt for DropIn eligibility.
        var now = match.WindowClosesAt.AddMinutes(1);
        var sut = CreateSut(now);

        var result = await sut.HandleAsync(match.Id, dropInPlayerId, request, CancellationToken.None);

        result.Status.Should().Be(PresenceStatus.Confirmed);
        result.ConfirmedAt.Should().NotBeNull();

        // Presence must be updated.
        await _presenceRepository.Received(1).UpdateAsync(presence, Arg.Any<CancellationToken>());

        // PresenceConfirmed event must be published.
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<PresenceConfirmed>(e =>
                e.MatchId == match.Id
                && e.PlayerId == dropInPlayerId
                && e.PlayerType == "DropIn"),
            Arg.Any<CancellationToken>());

        // Must NOT add to waiting list.
        await _waitingListRepository.DidNotReceive().AddAsync(
            Arg.Any<WaitingListEntry>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: F1.2 — DropIn confirm when all original DropInSlots are taken AND
    /// no released Regular slots exist → SlotLimitReachedException is thrown (409).
    /// Contrast with the test above where released slots make confirmation possible.
    /// </summary>
    [Fact]
    public async Task HandleAsync_DropIn_confirm_when_all_slots_taken_no_released_throws_SlotLimitReachedException()
    {
        // No released slots: ReleasedDropInSlots = 0.
        var match = BuildClosedMatchWithReleasedSlots(releasedSlots: 0);
        var dropInPlayerId = Guid.NewGuid();
        var presence = MatchPresence.Create(match.Id, dropInPlayerId, PlayerType.DropIn, FixedNow);

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, dropInPlayerId, Arg.Any<CancellationToken>())
            .Returns(presence);

        // All original DropInSlots (2) are taken; effective limit = 2 + 0 = 2.
        _presenceRepository.CountConfirmedAsync(match.Id, PlayerType.DropIn, Arg.Any<CancellationToken>())
            .Returns(2);

        _waitingListRepository.FindByMatchAndPlayerAsync(match.Id, dropInPlayerId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        _waitingListRepository.GetNextPositionAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns(1);

        var request = new UpdateMyPresenceRequest(Status: "Confirmed");
        var now = match.WindowClosesAt.AddMinutes(1);
        var sut = CreateSut(now);

        var act = async () => await sut.HandleAsync(match.Id, dropInPlayerId, request, CancellationToken.None);

        await act.Should().ThrowAsync<SlotLimitReachedException>();

        await _waitingListRepository.Received(1).AddAsync(
            Arg.Any<WaitingListEntry>(), Arg.Any<CancellationToken>());
    }

    // ─── AC: successful Regular confirmation publishes PresenceConfirmed ─────

    /// <summary>
    /// Covers: F1.2 — Confirming a Regular when slots are available persists Confirmed status
    /// and publishes PresenceConfirmed SQS event.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Regular_confirm_when_slot_available_returns_Confirmed_and_publishes_event()
    {
        var match = BuildOpenMatch(regularSlots: 2);
        var presence = MatchPresence.Create(match.Id, PlayerId, PlayerType.Regular, FixedNow);

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .Returns(presence);

        // Only 1 of 2 Regular slots is taken → slot available.
        _presenceRepository.CountConfirmedAsync(match.Id, PlayerType.Regular, Arg.Any<CancellationToken>())
            .Returns(1);

        var request = new UpdateMyPresenceRequest(Status: "Confirmed");
        var sut = CreateSut(FixedNow);

        var result = await sut.HandleAsync(match.Id, PlayerId, request, CancellationToken.None);

        result.Status.Should().Be(PresenceStatus.Confirmed);
        result.ConfirmedAt.Should().NotBeNull();

        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<PresenceConfirmed>(e =>
                e.MatchId == match.Id
                && e.PlayerId == PlayerId
                && e.PlayerType == "Regular"),
            Arg.Any<CancellationToken>());
    }

    // ─── Window eligibility: Regular outside window → exception ─────────────

    /// <summary>
    /// Covers: Regular player trying to confirm before WindowOpensAt throws PresenceWindowNotOpenException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Regular_confirm_before_window_opens_throws_PresenceWindowNotOpenException()
    {
        // Build match where window opens in the future.
        var windowOpensAt = FixedNow.AddHours(2);
        var windowClosesAt = FixedNow.AddHours(5);
        var match = Match.Create(
            organizerId: OrganizerId,
            name: "Test",
            description: null,
            address: "Addr",
            latitude: 0,
            longitude: 0,
            dateTime: FixedNow.AddDays(7),
            maxPlayers: 4,
            regularSlots: 2,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: windowOpensAt,
            windowClosesAt: windowClosesAt,
            now: FixedNow);

        typeof(Match).GetProperty(nameof(Match.Status))!.SetValue(match, MatchStatus.Open);

        var presence = MatchPresence.Create(match.Id, PlayerId, PlayerType.Regular, FixedNow);

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .Returns(presence);

        var request = new UpdateMyPresenceRequest(Status: "Confirmed");
        var sut = CreateSut(FixedNow); // now < windowOpensAt

        var act = async () => await sut.HandleAsync(match.Id, PlayerId, request, CancellationToken.None);

        await act.Should().ThrowAsync<PresenceWindowNotOpenException>()
            .WithMessage("*window*");
    }

    /// <summary>
    /// Covers: DropIn trying to confirm while match is still Open throws PresenceWindowNotOpenException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_DropIn_confirm_when_match_is_Open_throws_PresenceWindowNotOpenException()
    {
        var match = BuildOpenMatch();
        var dropInPlayerId = Guid.NewGuid();
        var presence = MatchPresence.Create(match.Id, dropInPlayerId, PlayerType.DropIn, FixedNow);

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, dropInPlayerId, Arg.Any<CancellationToken>())
            .Returns(presence);

        var request = new UpdateMyPresenceRequest(Status: "Confirmed");
        var sut = CreateSut(FixedNow);

        var act = async () => await sut.HandleAsync(match.Id, dropInPlayerId, request, CancellationToken.None);

        await act.Should().ThrowAsync<PresenceWindowNotOpenException>();
    }

    // ─── Decline promotes first Regular waiter ───────────────────────────────

    /// <summary>
    /// Covers: Declining a Regular presence triggers PromoteFirstWaiterAsync for Regular type.
    /// </summary>
    [Fact]
    public async Task HandleAsync_Regular_decline_promotes_first_Regular_waiter()
    {
        var match = BuildOpenMatch(regularSlots: 2);
        var presence = MatchPresence.Create(match.Id, PlayerId, PlayerType.Regular, FixedNow);
        presence.Confirm(FixedNow); // start as confirmed so decline is meaningful

        var waitingPlayerId = Guid.NewGuid();
        var waiterEntry = WaitingListEntry.Create(match.Id, waitingPlayerId, PlayerType.Regular, 1, FixedNow);

        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .Returns(presence);

        _waitingListRepository.ListByMatchAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns((IReadOnlyList<WaitingListEntry>)new List<WaitingListEntry> { waiterEntry });

        _presenceRepository.CountConfirmedAsync(match.Id, PlayerType.Regular, Arg.Any<CancellationToken>())
            .Returns(0);

        var request = new UpdateMyPresenceRequest(Status: "Declined");
        var sut = CreateSut(FixedNow);

        var result = await sut.HandleAsync(match.Id, PlayerId, request, CancellationToken.None);

        result.Status.Should().Be(PresenceStatus.Declined);

        // A new presence for the promoted waiter must be created.
        await _presenceRepository.Received(1).AddAsync(
            Arg.Is<MatchPresence>(p =>
                p.MatchId == match.Id
                && p.PlayerId == waitingPlayerId
                && p.PlayerType == PlayerType.Regular
                && p.Status == PresenceStatus.Pending),
            Arg.Any<CancellationToken>());

        // Waiting list entry must be removed.
        await _waitingListRepository.Received(1).RemoveAsync(waiterEntry, Arg.Any<CancellationToken>());
    }

    // ─── joining (no presence row yet) ───────────────────────────────────────

    /// <summary>
    /// Covers: F1.2 (mobile S12) — a player who is not on the list joins an open match by
    /// confirming: a Confirmed Regular presence is created and PresenceConfirmed is published.
    /// </summary>
    [Fact]
    public async Task HandleAsync_player_without_presence_joins_an_open_match()
    {
        var match = BuildOpenMatch();
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var sut = CreateSut(FixedNow);
        var presence = await sut.HandleAsync(
            match.Id, PlayerId, new UpdateMyPresenceRequest(Status: "Confirmed"), CancellationToken.None);

        presence.PlayerId.Should().Be(PlayerId);
        presence.PlayerType.Should().Be(PlayerType.Regular);
        presence.Status.Should().Be(PresenceStatus.Confirmed);
        await _presenceRepository.Received(1).AddAsync(presence, Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<PresenceConfirmed>(e => e.MatchId == match.Id && e.PlayerId == PlayerId),
            Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: declining a match you are not part of is not a join — PresenceNotFoundException.
    /// </summary>
    [Fact]
    public async Task HandleAsync_declining_without_presence_throws_PresenceNotFoundException()
    {
        var match = BuildOpenMatch();
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var sut = CreateSut(FixedNow);
        var act = async () => await sut.HandleAsync(
            match.Id, PlayerId, new UpdateMyPresenceRequest(Status: "Declined"), CancellationToken.None);

        await act.Should().ThrowAsync<PresenceNotFoundException>();
        await _presenceRepository.DidNotReceive().AddAsync(Arg.Any<MatchPresence>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: a private match by invite code — no code or a wrong code is refused
    /// (MatchInviteRequiredException); the right code (any casing) joins.
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("WRONGCODE", false)]
    [InlineData("exact", true)]
    [InlineData("lower", true)]
    public async Task HandleAsync_private_match_by_code_requires_the_invite_code(string? code, bool joins)
    {
        var match = MatchTestSupport.Match(
            OrganizerId, FixedNow, FixedNow.AddHours(-1), FixedNow.AddHours(1), MatchStatus.Open,
            settings: new MatchSettings(Visibility: MatchVisibility.Private, InviteMode: MatchInviteMode.Code));
        match.InviteCode.Should().HaveLength(8);
        var sent = code switch
        {
            "exact" => match.InviteCode,
            "lower" => match.InviteCode!.ToLowerInvariant(),
            _ => code,
        };
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var sut = CreateSut(FixedNow);
        var act = async () => await sut.HandleAsync(
            match.Id, PlayerId, new UpdateMyPresenceRequest("Confirmed", sent), CancellationToken.None);

        if (joins)
        {
            (await act.Should().NotThrowAsync()).Which.Status.Should().Be(PresenceStatus.Confirmed);
        }
        else
        {
            await act.Should().ThrowAsync<MatchInviteRequiredException>();
            await _presenceRepository.DidNotReceive().AddAsync(Arg.Any<MatchPresence>(), Arg.Any<CancellationToken>());
        }
    }

    /// <summary>
    /// Covers: a private "guests only" match never lets a stranger in, but its organizer can
    /// always put themselves on the list.
    /// </summary>
    [Fact]
    public async Task HandleAsync_private_guests_only_match_admits_only_the_organizer()
    {
        var match = MatchTestSupport.Match(
            OrganizerId, FixedNow, FixedNow.AddHours(-1), FixedNow.AddHours(1), MatchStatus.Open,
            settings: new MatchSettings(Visibility: MatchVisibility.Private, InviteMode: MatchInviteMode.Guests));
        match.InviteCode.Should().BeNull();
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .ReturnsNull();

        var sut = CreateSut(FixedNow);
        var stranger = async () => await sut.HandleAsync(
            match.Id, PlayerId, new UpdateMyPresenceRequest("Confirmed"), CancellationToken.None);
        var organizer = async () => await sut.HandleAsync(
            match.Id, OrganizerId, new UpdateMyPresenceRequest("Confirmed"), CancellationToken.None);

        await stranger.Should().ThrowAsync<MatchInviteRequiredException>();
        await organizer.Should().NotThrowAsync();
    }

    /// <summary>
    /// Covers: joining before the confirmation window opens is refused and creates nothing.
    /// </summary>
    [Fact]
    public async Task HandleAsync_joining_before_the_window_opens_throws_and_creates_nothing()
    {
        var match = MatchTestSupport.Match(
            OrganizerId, FixedNow, FixedNow.AddHours(2), FixedNow.AddDays(6), MatchStatus.Draft);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .ReturnsNull();

        var sut = CreateSut(FixedNow);
        var act = async () => await sut.HandleAsync(
            match.Id, PlayerId, new UpdateMyPresenceRequest("Confirmed"), CancellationToken.None);

        await act.Should().ThrowAsync<PresenceWindowNotOpenException>();
        await _presenceRepository.DidNotReceive().AddAsync(Arg.Any<MatchPresence>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: nothing opens the window but the clock — a Draft match whose window is already
    /// due is opened on the spot (MatchWindowOpened published) and the player joins.
    /// </summary>
    [Fact]
    public async Task HandleAsync_opens_a_due_window_before_checking_it()
    {
        var match = MatchTestSupport.Match(
            OrganizerId, FixedNow, FixedNow.AddMinutes(-5), FixedNow.AddDays(6), MatchStatus.Draft);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .ReturnsNull();
        _presenceRepository.ListByMatchAsync(match.Id, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<MatchPresence>());

        var sut = CreateSut(FixedNow);
        var presence = await sut.HandleAsync(
            match.Id, PlayerId, new UpdateMyPresenceRequest("Confirmed"), CancellationToken.None);

        match.Status.Should().Be(MatchStatus.Open);
        presence.Status.Should().Be(PresenceStatus.Confirmed);
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Is<MatchWindowOpened>(e => e.MatchId == match.Id), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Covers: guests occupy Regular slots — with 2 regular slots, 1 confirmed player and 1 guest
    /// the next player goes to the waiting list instead of joining.
    /// </summary>
    [Fact]
    public async Task HandleAsync_guests_count_against_the_regular_slots()
    {
        var match = BuildOpenMatch(regularSlots: 2);
        _matchRepository.FindByIdAsync(match.Id, Arg.Any<CancellationToken>()).Returns(match);
        _presenceRepository.FindByMatchAndPlayerAsync(match.Id, PlayerId, Arg.Any<CancellationToken>())
            .ReturnsNull();
        _presenceRepository.CountConfirmedAsync(match.Id, PlayerType.Regular, Arg.Any<CancellationToken>())
            .Returns(1);
        _guestRepository.CountByMatchAsync(match.Id, Arg.Any<CancellationToken>()).Returns(1);
        _waitingListRepository.GetNextPositionAsync(match.Id, Arg.Any<CancellationToken>()).Returns(1);

        var sut = CreateSut(FixedNow);
        var act = async () => await sut.HandleAsync(
            match.Id, PlayerId, new UpdateMyPresenceRequest("Confirmed"), CancellationToken.None);

        (await act.Should().ThrowAsync<SlotLimitReachedException>()).Which.WaitingListPosition.Should().Be(1);
        await _presenceRepository.DidNotReceive().AddAsync(Arg.Any<MatchPresence>(), Arg.Any<CancellationToken>());
        await _waitingListRepository.Received(1).AddAsync(
            Arg.Is<WaitingListEntry>(w => w.PlayerId == PlayerId && w.PlayerType == PlayerType.Regular),
            Arg.Any<CancellationToken>());
    }

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
