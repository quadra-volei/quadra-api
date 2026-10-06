using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using NSubstitute;
using Quadra.Modules.Geo.Application;
using Quadra.Modules.Geo.Contracts;
using Quadra.Modules.Geo.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.UnitTests.Modules.Geo;

/// <summary>
/// Unit tests for <see cref="GetNearbyMatchesHandler"/> — the application-code merge of the
/// PostGIS spatial candidates with per-match slot availability (via <see cref="IMatchAvailabilityReader"/>).
///
/// Covers the F1.7 filtering / ordering acceptance criteria in isolation (the PostGIS radius filter
/// itself is exercised against real Postgres in the integration tests):
///   - AC-1: only matches with OpenSlots &gt; 0 are returned, ordered by DistanceKm ascending.
///   - AC-2: openToDropIns=true keeps only HasOpenDropInSlot matches; openToDropIns=false keeps all
///           matches with open slots.
/// </summary>
public sealed class GetNearbyMatchesHandlerTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 7, 1, 12, 0, 0, TimeSpan.Zero);

    private readonly INearbyMatchRepository _repository = Substitute.For<INearbyMatchRepository>();
    private readonly IMatchAvailabilityReader _availabilityReader = Substitute.For<IMatchAvailabilityReader>();
    private readonly TimeProvider _timeProvider = new FixedTimeProvider(FixedNow);

    private GetNearbyMatchesHandler CreateSut() =>
        new(_repository, _availabilityReader, _timeProvider, NullLogger<GetNearbyMatchesHandler>.Instance);

    // ─── AC-1: matches with no open slots are excluded ───────────────────────

    /// <summary>
    /// Covers: AC-1 — "returns matches within the radius whose OpenSlots &gt; 0; matches with no open
    /// slots are excluded from Items". A candidate whose availability reports OpenSlots == 0 is dropped.
    /// </summary>
    [Fact]
    public async Task HandleAsync_excludes_candidates_with_no_open_slots()
    {
        var withSlots = BuildCandidate("Has Slots", distanceMeters: 1000);
        var noSlots = BuildCandidate("Full", distanceMeters: 2000);

        SetupCandidates(withSlots, noSlots);
        SetupAvailability(
            Availability(withSlots.Id, maxPlayers: 10, confirmed: 4, openSlots: 6, hasOpenDropIn: true),
            Availability(noSlots.Id, maxPlayers: 10, confirmed: 10, openSlots: 0, hasOpenDropIn: false));

        var result = await CreateSut().HandleAsync(Query(), CancellationToken.None);

        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(withSlots.Id);
        result.Count.Should().Be(1);
    }

    /// <summary>
    /// Covers: AC-1 — Items are ordered by DistanceKm ascending, preserving the distance-sorted order
    /// produced by the spatial repository.
    /// </summary>
    [Fact]
    public async Task HandleAsync_returns_items_ordered_by_distance_ascending()
    {
        var near = BuildCandidate("Near", distanceMeters: 500);
        var mid = BuildCandidate("Mid", distanceMeters: 1500);
        var far = BuildCandidate("Far", distanceMeters: 3200);

        // Repository returns them already ordered ascending by distance.
        SetupCandidates(near, mid, far);
        SetupAvailability(
            Availability(near.Id, 10, 2, 8, true),
            Availability(mid.Id, 10, 2, 8, true),
            Availability(far.Id, 10, 2, 8, true));

        var result = await CreateSut().HandleAsync(Query(), CancellationToken.None);

        result.Items.Select(i => i.Id).Should().ContainInOrder(near.Id, mid.Id, far.Id);
        result.Items.Select(i => i.DistanceKm).Should().BeInAscendingOrder();
        result.Items[0].DistanceKm.Should().Be(0.5);
        result.Items[2].DistanceKm.Should().Be(3.2);
    }

    /// <summary>
    /// Covers: AC-1 — DistanceKm is the straight-line metres/1000 rounded to 3 decimals, and OpenSlots
    /// is taken from the availability reader.
    /// </summary>
    [Fact]
    public async Task HandleAsync_maps_distance_km_rounded_to_three_decimals_and_open_slots()
    {
        var candidate = BuildCandidate("Precise", distanceMeters: 1234.5678);
        SetupCandidates(candidate);
        SetupAvailability(Availability(candidate.Id, maxPlayers: 12, confirmed: 5, openSlots: 7, hasOpenDropIn: true));

        var result = await CreateSut().HandleAsync(Query(), CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Subject;
        item.DistanceKm.Should().Be(1.235);
        item.OpenSlots.Should().Be(7);
        item.OpenToDropIns.Should().BeTrue();
    }

    /// <summary>
    /// Covers: AC-1 — a candidate returned by the spatial query but absent from the availability
    /// dictionary (e.g. deleted between the two reads) is skipped rather than throwing.
    /// </summary>
    [Fact]
    public async Task HandleAsync_skips_candidate_missing_from_availability()
    {
        var present = BuildCandidate("Present", distanceMeters: 1000);
        var missing = BuildCandidate("Missing", distanceMeters: 2000);

        SetupCandidates(present, missing);
        // Only 'present' has availability; 'missing' is omitted from the dictionary.
        SetupAvailability(Availability(present.Id, 10, 2, 8, true));

        var result = await CreateSut().HandleAsync(Query(), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Id.Should().Be(present.Id);
    }

    /// <summary>
    /// Covers: AC-1 — no spatial candidates yields an empty, well-formed response without calling the
    /// availability reader.
    /// </summary>
    [Fact]
    public async Task HandleAsync_returns_empty_response_when_no_candidates()
    {
        _repository
            .FindWithinRadiusAsync(Arg.Any<Point>(), Arg.Any<double>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<NearbyMatchCandidate>());

        var result = await CreateSut().HandleAsync(Query(), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.Count.Should().Be(0);
        await _availabilityReader.DidNotReceiveWithAnyArgs()
            .GetAvailabilityAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>());
    }

    // ─── AC-2: filter by matches open to DropIns ─────────────────────────────

    /// <summary>
    /// Covers: AC-2 — with openToDropIns=true only matches where a DropIn can still join
    /// (HasOpenDropInSlot == true) are returned; a match that has open Regular slots but no open
    /// DropIn slot is excluded.
    /// </summary>
    [Fact]
    public async Task HandleAsync_openToDropIns_true_returns_only_matches_with_open_dropin_slot()
    {
        var openDropIn = BuildCandidate("Open DropIn", distanceMeters: 1000);
        var noDropIn = BuildCandidate("No DropIn", distanceMeters: 2000);

        SetupCandidates(openDropIn, noDropIn);
        SetupAvailability(
            // Both have open slots overall, but only the first can still take a DropIn.
            Availability(openDropIn.Id, maxPlayers: 4, confirmed: 0, openSlots: 4, hasOpenDropIn: true),
            Availability(noDropIn.Id, maxPlayers: 4, confirmed: 2, openSlots: 2, hasOpenDropIn: false));

        var result = await CreateSut().HandleAsync(Query(openToDropIns: true), CancellationToken.None);

        result.Items.Should().ContainSingle().Which.Id.Should().Be(openDropIn.Id);
    }

    /// <summary>
    /// Covers: AC-2 — with openToDropIns=false (default) all matches with open slots are returned
    /// regardless of DropIn availability.
    /// </summary>
    [Fact]
    public async Task HandleAsync_openToDropIns_false_returns_all_matches_with_open_slots()
    {
        var openDropIn = BuildCandidate("Open DropIn", distanceMeters: 1000);
        var noDropIn = BuildCandidate("No DropIn", distanceMeters: 2000);

        SetupCandidates(openDropIn, noDropIn);
        SetupAvailability(
            Availability(openDropIn.Id, maxPlayers: 4, confirmed: 0, openSlots: 4, hasOpenDropIn: true),
            Availability(noDropIn.Id, maxPlayers: 4, confirmed: 2, openSlots: 2, hasOpenDropIn: false));

        var result = await CreateSut().HandleAsync(Query(openToDropIns: false), CancellationToken.None);

        result.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { openDropIn.Id, noDropIn.Id });
    }

    // ─── helpers ──────────────────────────────────────────────────────────────

    private static NearbyMatchesQuery Query(bool openToDropIns = false) =>
        new(Lat: -23.5505, Lon: -46.6333, RadiusKm: 10, OpenToDropIns: openToDropIns);

    private void SetupCandidates(params NearbyMatchCandidate[] candidates) =>
        _repository
            .FindWithinRadiusAsync(Arg.Any<Point>(), Arg.Any<double>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(candidates);

    private void SetupAvailability(params MatchAvailability[] availabilities) =>
        _availabilityReader
            .GetAvailabilityAsync(Arg.Any<IReadOnlyCollection<Guid>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<Guid, MatchAvailability>)availabilities.ToDictionary(a => a.MatchId));

    private static MatchAvailability Availability(
        Guid id, int maxPlayers, int confirmed, int openSlots, bool hasOpenDropIn) =>
        new(id, maxPlayers, confirmed, openSlots, hasOpenDropIn);

    private static NearbyMatchCandidate BuildCandidate(string name, double distanceMeters) =>
        new(
            Id: Guid.NewGuid(),
            Name: name,
            Address: "Rua Teste, 123",
            Latitude: -23.55,
            Longitude: -46.63,
            DateTime: FixedNow.AddDays(3),
            MaxPlayers: 12,
            Price: null,
            Format: "4X4",
            Level: "Intermediate",
            Type: "OneOff",
            Status: "Open",
            DistanceMeters: distanceMeters);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }
}
