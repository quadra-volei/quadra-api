using FluentAssertions;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Entities;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.UnitTests.Modules.Matches;

/// <summary>
/// Unit tests for <see cref="Match"/> entity — factory method and state-machine transitions.
/// </summary>
public sealed class MatchEntityTests
{
    private static readonly DateTimeOffset Now = new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid OrganizerId = Guid.NewGuid();

    // Helper to create a valid OneOff match for state-transition tests.
    private static Match CreateDraftMatch(MatchStatus? overrideStatus = null)
    {
        var match = Match.Create(
            organizerId: OrganizerId,
            name: "Sunday Volleyball",
            description: null,
            address: "Rua Teste, 123",
            latitude: -23.5505,
            longitude: -46.6333,
            dateTime: Now.AddDays(7),
            maxPlayers: 12,
            regularSlots: 10,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: Now.AddDays(1),
            windowClosesAt: Now.AddDays(6),
            now: Now);

        // Force specific status via reflection for state-machine tests.
        if (overrideStatus.HasValue)
        {
            var prop = typeof(Match).GetProperty(nameof(Match.Status))!;
            prop.SetValue(match, overrideStatus.Value);
        }

        return match;
    }

    // ─── Match.Create ───────────────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-1 — initial status must be Draft.
    /// </summary>
    [Fact]
    public void Create_OneOff_match_sets_status_to_Draft()
    {
        var match = CreateDraftMatch();

        match.Status.Should().Be(MatchStatus.Draft);
    }

    /// <summary>
    /// Covers: AC-1 — Location Point built correctly (X=Longitude, Y=Latitude).
    /// </summary>
    [Fact]
    public void Create_sets_Location_point_with_correct_coordinates()
    {
        const double lat = -23.5505;
        const double lon = -46.6333;

        var match = Match.Create(
            organizerId: OrganizerId,
            name: "Test",
            description: null,
            address: "Addr",
            latitude: lat,
            longitude: lon,
            dateTime: Now.AddDays(7),
            maxPlayers: 10,
            regularSlots: 8,
            price: null,
            type: MatchType.OneOff,
            frequency: null,
            dayOfWeekIso: null,
            windowOpensAt: Now.AddDays(1),
            windowClosesAt: Now.AddDays(6),
            now: Now);

        match.Location.Y.Should().Be(lat);   // Y = latitude
        match.Location.X.Should().Be(lon);   // X = longitude
        match.Location.SRID.Should().Be(4326);
    }

    /// <summary>
    /// Covers: AC-2 — Recurring match stores Frequency and DayOfWeek correctly.
    /// </summary>
    [Fact]
    public void Create_Recurring_match_stores_frequency_and_dayOfWeek()
    {
        var match = Match.Create(
            organizerId: OrganizerId,
            name: "Weekly Volleyball",
            description: null,
            address: "Addr",
            latitude: 0,
            longitude: 0,
            dateTime: Now.AddDays(7),
            maxPlayers: 12,
            regularSlots: 10,
            price: null,
            type: MatchType.Recurring,
            frequency: MatchFrequency.Weekly,
            dayOfWeekIso: 3,
            windowOpensAt: Now.AddDays(1),
            windowClosesAt: Now.AddDays(6),
            now: Now);

        match.Type.Should().Be(MatchType.Recurring);
        match.Frequency.Should().Be(MatchFrequency.Weekly);
        match.DayOfWeek.Should().Be(3);
    }

    /// <summary>
    /// Covers: Match.Create assigns a new non-empty Guid as Id.
    /// </summary>
    [Fact]
    public void Create_assigns_non_empty_Id()
    {
        var match = CreateDraftMatch();
        match.Id.Should().NotBeEmpty();
    }

    /// <summary>
    /// Covers: Match.Create stores CreatedAt and UpdatedAt equal to the provided now.
    /// </summary>
    [Fact]
    public void Create_sets_CreatedAt_and_UpdatedAt_to_now()
    {
        var match = CreateDraftMatch();

        match.CreatedAt.Should().Be(Now);
        match.UpdatedAt.Should().Be(Now);
    }

    // ─── Match.Cancel ───────────────────────────────────────────────────────

    /// <summary>
    /// Covers: AC-12 — Cancel on a Draft match succeeds and transitions to Cancelled.
    /// </summary>
    [Fact]
    public void Cancel_Draft_match_transitions_to_Cancelled()
    {
        var match = CreateDraftMatch();
        var cancelledAt = Now.AddHours(1);

        match.Cancel(cancelledAt);

        match.Status.Should().Be(MatchStatus.Cancelled);
        match.UpdatedAt.Should().Be(cancelledAt);
    }

    /// <summary>
    /// Covers: AC-14 — Cancel on an InProgress match throws InvalidMatchStatusTransitionException.
    /// </summary>
    [Fact]
    public void Cancel_InProgress_match_throws_InvalidMatchStatusTransitionException()
    {
        var match = CreateDraftMatch(MatchStatus.InProgress);

        var act = () => match.Cancel(Now);

        act.Should().Throw<InvalidMatchStatusTransitionException>()
            .WithMessage("*InProgress*");
    }

    /// <summary>
    /// Covers: Cancel on an Ended match throws InvalidMatchStatusTransitionException.
    /// </summary>
    [Fact]
    public void Cancel_Ended_match_throws_InvalidMatchStatusTransitionException()
    {
        var match = CreateDraftMatch(MatchStatus.Ended);

        var act = () => match.Cancel(Now);

        act.Should().Throw<InvalidMatchStatusTransitionException>()
            .WithMessage("*Ended*");
    }

    /// <summary>
    /// Covers: Cancel on an Open match succeeds (Open is not a terminal state).
    /// </summary>
    [Fact]
    public void Cancel_Open_match_transitions_to_Cancelled()
    {
        var match = CreateDraftMatch(MatchStatus.Open);

        match.Cancel(Now);

        match.Status.Should().Be(MatchStatus.Cancelled);
    }
}
