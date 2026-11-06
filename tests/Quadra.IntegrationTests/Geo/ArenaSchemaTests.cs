using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Quadra.IntegrationTests.Matches;
using Quadra.Modules.Geo.Arenas;
using Quadra.Modules.Geo.Persistence;
using Quadra.Modules.Geo.Venues;
using Quadra.Modules.Matches.Application;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using MatchType = Quadra.Modules.Matches.Entities.MatchType;

namespace Quadra.IntegrationTests.Geo;

/// <summary>
/// The curated arenas (<c>arenas</c>, <c>arena_places</c>) and <c>matches.arena_id</c> against real
/// PostGIS, migrated in boot order (Matches, then Geo, which adds the foreign key). The database
/// itself refuses an orphan link, a place in two arenas and a state that breaks its rules.
/// </summary>
public sealed class ArenaSchemaTests : IClassFixture<MatchesWebApplicationFactory>
{
    private const double Lat = -23.5505;
    private const double Lon = -46.6333;

    // Literal SQL, so the machine culture cannot turn the decimal point into a comma.
    private const string PointSql = "ST_SetSRID(ST_MakePoint(-46.6333, -23.5505), 4326)::geography";

    private readonly MatchesWebApplicationFactory _fx;

    public ArenaSchemaTests(MatchesWebApplicationFactory fx)
    {
        _fx = fx;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Matches_point_at_arenas_through_a_restrict_foreign_key()
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "SELECT confdeltype::text FROM pg_constraint WHERE conname = 'fk_matches_arenas_arena_id' AND conrelid = 'matches'::regclass AND confrelid = 'arenas'::regclass",
            conn);

        (await cmd.ExecuteScalarAsync(Ct)).Should().Be("r", "ON DELETE RESTRICT");
    }

    [Fact]
    public async Task A_game_links_only_to_an_existing_arena_and_keeps_it_from_being_deleted()
    {
        var arena = await SaveActiveArenaAsync();
        var match = await SeedMatchAsync(MatchType.OneOff);

        (await RefusedAsync($"UPDATE matches SET arena_id = '{Guid.NewGuid()}' WHERE id = '{match.Id}'"))
            .ConstraintName.Should().Be("fk_matches_arenas_arena_id");

        await ExecAsync($"UPDATE matches SET arena_id = '{arena.Id}' WHERE id = '{match.Id}'");
        (await RefusedAsync($"DELETE FROM arenas WHERE id = '{arena.Id}'"))
            .ConstraintName.Should().Be("fk_matches_arenas_arena_id");

        await ExecAsync($"UPDATE matches SET arena_id = NULL WHERE id = '{match.Id}'");
        (await CountAsync($"SELECT count(*) FROM matches m WHERE m.arena_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM arenas a WHERE a.id = m.arena_id)"))
            .Should().Be(0, "no game points at a missing arena");
    }

    [Fact]
    public async Task A_place_belongs_to_one_arena_at_most()
    {
        var first = await SaveActiveArenaAsync();
        var second = await SaveActiveArenaAsync();
        var placeId = $"place-{Guid.NewGuid():N}";
        await SaveAsync(ArenaPlace.Link(placeId, first.Id, isPrimary: true, Guid.NewGuid(), DateTimeOffset.UtcNow));

        (await RefusedAsync(PlaceSql(placeId, second.Id, ArenaPlaceStatuses.Linked, isPrimary: false)))
            .ConstraintName.Should().Be("PK_arena_places");
    }

    [Fact]
    public async Task Place_rows_follow_their_status()
    {
        var arena = await SaveActiveArenaAsync();
        await SaveAsync(ArenaPlace.Link($"place-{Guid.NewGuid():N}", arena.Id, isPrimary: true, Guid.NewGuid(), DateTimeOffset.UtcNow));
        await SaveAsync(ArenaPlace.Link($"place-{Guid.NewGuid():N}", arena.Id, isPrimary: false, Guid.NewGuid(), DateTimeOffset.UtcNow));
        await SaveAsync(ArenaPlace.Reject($"place-{Guid.NewGuid():N}", Guid.NewGuid(), DateTimeOffset.UtcNow));

        (await RefusedAsync(PlaceSql(NewPlace(), null, ArenaPlaceStatuses.Linked, isPrimary: false)))
            .ConstraintName.Should().Be("ck_arena_places_status", "a linked place needs its arena");
        (await RefusedAsync(PlaceSql(NewPlace(), arena.Id, ArenaPlaceStatuses.Rejected, isPrimary: false)))
            .ConstraintName.Should().Be("ck_arena_places_status", "a rejected place belongs to no arena");
        (await RefusedAsync(PlaceSql(NewPlace(), null, ArenaPlaceStatuses.Rejected, isPrimary: true)))
            .ConstraintName.Should().Be("ck_arena_places_primary");
        (await RefusedAsync(PlaceSql(NewPlace(), arena.Id, ArenaPlaceStatuses.Linked, isPrimary: true)))
            .ConstraintName.Should().Be("uq_arena_places_primary", "one main entry per arena");
        (await RefusedAsync(PlaceSql(NewPlace(), Guid.NewGuid(), ArenaPlaceStatuses.Linked, isPrimary: false)))
            .ConstraintName.Should().Be("fk_arena_places_arenas_arena_id");
    }

    [Fact]
    public async Task Arena_rows_follow_their_status()
    {
        var target = await SaveActiveArenaAsync();
        var merged = Arena.Create(Lat, Lon, VenueKinds.Sand, DateTimeOffset.UtcNow);
        merged.MergeInto(target, Guid.NewGuid(), DateTimeOffset.UtcNow);
        await SaveAsync(merged);
        await SaveAsync(Arena.Create(Lat, Lon, VenueKinds.Park, DateTimeOffset.UtcNow));

        var reviewer = Guid.NewGuid();
        (await RefusedAsync(ArenaSql(Guid.NewGuid(), "Beach", ArenaStatuses.Draft, null, null)))
            .ConstraintName.Should().Be("ck_arenas_kind");
        (await RefusedAsync(ArenaSql(Guid.NewGuid(), VenueKinds.Court, "Pending", null, reviewer)))
            .ConstraintName.Should().Be("ck_arenas_status");
        (await RefusedAsync(ArenaSql(Guid.NewGuid(), VenueKinds.Court, ArenaStatuses.Merged, null, reviewer)))
            .ConstraintName.Should().Be("ck_arenas_merged_into", "a merged arena points at the one that stays");
        (await RefusedAsync(ArenaSql(Guid.NewGuid(), VenueKinds.Court, ArenaStatuses.Active, target.Id, reviewer)))
            .ConstraintName.Should().Be("ck_arenas_merged_into", "only a merged arena has a target");
        var self = Guid.NewGuid();
        (await RefusedAsync(ArenaSql(self, VenueKinds.Court, ArenaStatuses.Merged, self, reviewer)))
            .ConstraintName.Should().Be("ck_arenas_merged_into", "never merged into itself");
        (await RefusedAsync(ArenaSql(Guid.NewGuid(), VenueKinds.Court, ArenaStatuses.Active, null, null)))
            .ConstraintName.Should().Be("ck_arenas_reviewed", "approval is a person's decision");
        (await RefusedAsync(ArenaSql(Guid.NewGuid(), VenueKinds.Court, ArenaStatuses.Merged, Guid.NewGuid(), reviewer)))
            .ConstraintName.Should().Be("fk_arenas_arenas_merged_into_id");

        using var scope = _fx.Factory.Services.CreateScope();
        var saved = await scope.ServiceProvider.GetRequiredService<GeoDbContext>().Arenas
            .SingleAsync(a => a.Id == merged.Id, Ct);
        saved.Status.Should().Be(ArenaStatuses.Merged);
        saved.MergedIntoId.Should().Be(target.Id);
        saved.Kind.Should().Be(VenueKinds.Sand);
    }

    [Fact]
    public async Task The_venue_purge_leaves_arenas_and_their_places()
    {
        await ExecAsync("TRUNCATE TABLE venues, venue_areas;");
        var arena = await SaveActiveArenaAsync();
        var old = NewPlace();
        await ExecAsync(VenueSql(old, fetchedDaysAgo: 40));
        await ExecAsync(VenueSql(NewPlace(), fetchedDaysAgo: 1));
        await SaveAsync(ArenaPlace.Link(old, arena.Id, isPrimary: true, Guid.NewGuid(), DateTimeOffset.UtcNow));
        await SaveAsync(ArenaPlace.Reject(NewPlace(), Guid.NewGuid(), DateTimeOffset.UtcNow));
        var arenas = await CountAsync("SELECT count(*) FROM arenas");
        var places = await CountAsync("SELECT count(*) FROM arena_places");

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            (await scope.ServiceProvider.GetRequiredService<VenueDiscovery>().PurgeExpiredAsync(Ct)).Should().Be(1);
        }

        (await CountAsync("SELECT count(*) FROM venues")).Should().Be(1);
        (await CountAsync("SELECT count(*) FROM arenas")).Should().Be(arenas);
        (await CountAsync("SELECT count(*) FROM arena_places")).Should().Be(places);
        (await CountAsync($"SELECT count(*) FROM arena_places WHERE place_id = '{old}' AND arena_id = '{arena.Id}'"))
            .Should().Be(1, "the link outlives the purged coordinates");
    }

    [Fact]
    public async Task The_next_game_of_a_recurring_group_keeps_the_arena()
    {
        var arena = await SaveActiveArenaAsync();
        var first = await SeedMatchAsync(MatchType.Recurring);
        var loose = await SeedMatchAsync(MatchType.Recurring);
        await ExecAsync($"UPDATE matches SET arena_id = '{arena.Id}' WHERE id = '{first.Id}'");

        using (var scope = _fx.Factory.Services.CreateScope())
        {
            var matches = scope.ServiceProvider.GetRequiredService<IMatchRepository>();
            var schedule = scope.ServiceProvider.GetRequiredService<ScheduleNextGameHandler>();
            await schedule.ScheduleAsync((await matches.FindByIdAsync(first.Id, Ct))!, carryConfirmed: false, Ct);
            await schedule.ScheduleAsync((await matches.FindByIdAsync(loose.Id, Ct))!, carryConfirmed: false, Ct);
        }

        using var read = _fx.Factory.Services.CreateScope();
        var db = read.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var next = await db.Matches.AsNoTracking().SingleAsync(m => m.GroupId == first.Id, Ct);
        next.ArenaId.Should().Be(arena.Id);
        (await db.Matches.AsNoTracking().SingleAsync(m => m.GroupId == loose.Id, Ct))
            .ArenaId.Should().BeNull("a game with no arena stays without one");
    }

    private static string NewPlace() => $"place-{Guid.NewGuid():N}";

    private static string PlaceSql(string placeId, Guid? arenaId, string status, bool isPrimary) =>
        $"INSERT INTO arena_places (place_id, arena_id, status, is_primary, reviewed_by, reviewed_at) " +
        $"VALUES ('{placeId}', {(arenaId is null ? "NULL" : $"'{arenaId}'")}, '{status}', {isPrimary}, '{Guid.NewGuid()}', now())";

    private static string ArenaSql(Guid id, string kind, string status, Guid? mergedInto, Guid? reviewer) =>
        $"INSERT INTO arenas (id, location, kind, status, merged_into_id, reviewed_by, reviewed_at) " +
        $"VALUES ('{id}', {PointSql}, '{kind}', '{status}', " +
        $"{(mergedInto is null ? "NULL" : $"'{mergedInto}'")}, {(reviewer is null ? "NULL, NULL" : $"'{reviewer}', now()")})";

    private static string VenueSql(string placeId, int fetchedDaysAgo) =>
        $"INSERT INTO venues (place_id, location, fetched_at, kind) " +
        $"VALUES ('{placeId}', {PointSql}, now() - interval '{fetchedDaysAgo} days', 'Park')";

    private async Task<Arena> SaveActiveArenaAsync()
    {
        var arena = Arena.Create(Lat, Lon, VenueKinds.Court, DateTimeOffset.UtcNow);
        arena.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow);
        await SaveAsync(arena);
        return arena;
    }

    private async Task SaveAsync(object entity)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoDbContext>();
        db.Add(entity);
        await db.SaveChangesAsync(Ct);
    }

    private async Task<Match> SeedMatchAsync(MatchType type)
    {
        using var scope = _fx.Factory.Services.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<MatchesDbContext>();
        var now = DateTimeOffset.UtcNow;
        var recurring = type == MatchType.Recurring;
        var match = Match.Create(
            organizerId: Guid.NewGuid(),
            name: "Racha da arena",
            description: null,
            address: "Rua Teste, 123",
            latitude: Lat,
            longitude: Lon,
            dateTime: now.AddDays(7),
            maxPlayers: 12,
            regularSlots: 12,
            price: null,
            type: type,
            frequency: recurring ? MatchFrequency.Weekly : null,
            dayOfWeekIso: recurring ? 2 : null,
            windowOpensAt: now.AddDays(1),
            windowClosesAt: now.AddDays(5),
            now: now);
        ctx.Matches.Add(match);
        await ctx.SaveChangesAsync(Ct);
        return match;
    }

    private async Task<NpgsqlConnection> OpenAsync()
    {
        // The configured string: EF's copy drops the password once a connection has opened.
        var conn = new NpgsqlConnection(
            _fx.Factory.Services.GetRequiredService<IConfiguration>().GetConnectionString("Default"));
        await conn.OpenAsync(Ct);
        return conn;
    }

    private async Task ExecAsync(string sql)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync(Ct);
    }

    private async Task<long> CountAsync(string sql)
    {
        await using var conn = await OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return (long)(await cmd.ExecuteScalarAsync(Ct))!;
    }

    private async Task<PostgresException> RefusedAsync(string sql)
    {
        var act = () => ExecAsync(sql);
        return (await act.Should().ThrowAsync<PostgresException>()).Which;
    }
}
