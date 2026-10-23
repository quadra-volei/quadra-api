using FluentAssertions;
using Quadra.Modules.Geo.Arenas;
using Quadra.Modules.Geo.Venues;

namespace Quadra.UnitTests.Modules.Geo;

/// <summary>Curated arenas: what each state allows, and what a place id decision records.</summary>
public sealed class ArenaTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid Reviewer = Guid.NewGuid();

    private static Arena New(string kind = VenueKinds.Court) => Arena.Create(-23.5505, -46.6333, kind, Now);

    private static Arena Active()
    {
        var arena = New();
        arena.Approve(Reviewer, Now);
        return arena;
    }

    [Fact]
    public void A_new_arena_is_a_draft_with_our_own_point_and_no_reviewer()
    {
        var arena = New(VenueKinds.Sand);

        arena.Status.Should().Be(ArenaStatuses.Draft);
        arena.Kind.Should().Be(VenueKinds.Sand);
        arena.Location.X.Should().Be(-46.6333, "X is the longitude");
        arena.Location.SRID.Should().Be(4326);
        arena.ReviewedBy.Should().BeNull();
        arena.MergedIntoId.Should().BeNull();
    }

    [Fact]
    public void An_unknown_kind_is_refused()
    {
        var act = () => New("Beach");

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Approving_and_deactivating_record_who_decided()
    {
        var arena = New();
        var later = Now.AddHours(1);

        arena.Approve(Reviewer, Now);
        arena.Status.Should().Be(ArenaStatuses.Active);
        arena.ReviewedBy.Should().Be(Reviewer);
        arena.ReviewedAt.Should().Be(Now);

        arena.Deactivate(Reviewer, later);
        arena.Status.Should().Be(ArenaStatuses.Inactive);
        arena.ReviewedAt.Should().Be(later);

        arena.Approve(Reviewer, later);
        arena.Status.Should().Be(ArenaStatuses.Active, "an inactive arena can come back");
    }

    [Fact]
    public void Approving_an_active_arena_or_deactivating_an_inactive_one_is_refused()
    {
        var active = Active();
        var inactive = Active();
        inactive.Deactivate(Reviewer, Now);

        ((Action)(() => active.Approve(Reviewer, Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => inactive.Deactivate(Reviewer, Now))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Merging_points_at_the_arena_that_stays()
    {
        var stays = Active();
        var duplicate = New();

        duplicate.MergeInto(stays, Reviewer, Now);

        duplicate.Status.Should().Be(ArenaStatuses.Merged);
        duplicate.MergedIntoId.Should().Be(stays.Id);
        duplicate.ReviewedBy.Should().Be(Reviewer);
    }

    [Fact]
    public void An_arena_is_never_merged_into_itself_a_draft_or_twice()
    {
        var arena = Active();
        var draft = New();
        var merged = New();
        merged.MergeInto(Active(), Reviewer, Now);

        ((Action)(() => arena.MergeInto(arena, Reviewer, Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => arena.MergeInto(draft, Reviewer, Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => merged.MergeInto(arena, Reviewer, Now))).Should().Throw<InvalidOperationException>();
        ((Action)(() => merged.Approve(Reviewer, Now))).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void A_linked_place_names_its_arena_and_a_rejected_one_none()
    {
        var arenaId = Guid.NewGuid();

        var linked = ArenaPlace.Link("place-a", arenaId, isPrimary: true, Reviewer, Now);
        var rejected = ArenaPlace.Reject("place-b", Reviewer, Now);

        linked.Status.Should().Be(ArenaPlaceStatuses.Linked);
        linked.ArenaId.Should().Be(arenaId);
        linked.IsPrimary.Should().BeTrue();
        rejected.Status.Should().Be(ArenaPlaceStatuses.Rejected);
        rejected.ArenaId.Should().BeNull();
        rejected.IsPrimary.Should().BeFalse();
        rejected.ReviewedBy.Should().Be(Reviewer);
    }
}
