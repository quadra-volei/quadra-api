using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Application;
using Quadra.Modules.Profile.Persistence;
using Quadra.Modules.Profile.Posts;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Profile.Activities;

// "Atividade dos amigos": what the players I follow did — started or finished a match, were its
// MVP, reached a new level number. Recorded automatically from the game events (never posted to
// the Rede), only for open matches. Who sees what is decided at every read: the actor must be
// followed by the caller and share their activity, the match must still be open, "started" only
// while the game is on, and nothing older than 30 days.

public enum ActivityType
{
    MatchStarted,
    MatchEnded,
    MvpAwarded,
    LevelUp,
}

/// <summary>One thing a player did. At most one per actor and <see cref="DedupKey"/>.</summary>
public sealed class Activity
{
    public Guid Id { get; init; }

    public Guid ActorId { get; init; }

    public ActivityType Type { get; init; }

    /// <summary>The match it happened in; null for a level up.</summary>
    public Guid? MatchId { get; init; }

    /// <summary>The match's name when it happened.</summary>
    public string? MatchName { get; init; }

    /// <summary><c>Win</c>, <c>Loss</c> or <c>Draw</c>, from the actor's side (finished match).</summary>
    public string? Outcome { get; init; }

    public int? SetsWon { get; init; }

    public int? SetsLost { get; init; }

    public int? LevelFrom { get; init; }

    public int? LevelTo { get; init; }

    public DateTimeOffset OccurredAt { get; init; }

    /// <summary><c>{Type}:{MatchId}</c>, or <c>LevelUp:{LevelTo}</c>: events arrive at least once.</summary>
    public string DedupKey { get; init; } = null!;
}

public sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("activities");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(a => a.ActorId).HasColumnName("actor_id").IsRequired();
        builder.Property(a => a.Type).HasColumnName("type").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.MatchId).HasColumnName("match_id");
        builder.Property(a => a.MatchName).HasColumnName("match_name").HasMaxLength(200);
        builder.Property(a => a.Outcome).HasColumnName("outcome").HasMaxLength(10);
        builder.Property(a => a.SetsWon).HasColumnName("sets_won");
        builder.Property(a => a.SetsLost).HasColumnName("sets_lost");
        builder.Property(a => a.LevelFrom).HasColumnName("level_from");
        builder.Property(a => a.LevelTo).HasColumnName("level_to");
        builder.Property(a => a.OccurredAt).HasColumnName("occurred_at").IsRequired();
        builder.Property(a => a.DedupKey).HasColumnName("dedup_key").HasMaxLength(64).IsRequired();

        builder.HasIndex(a => new { a.ActorId, a.DedupKey }).IsUnique().HasDatabaseName("ux_activities_actor_id_dedup_key");
        builder.HasIndex(a => new { a.ActorId, a.OccurredAt }).HasDatabaseName("ix_activities_actor_id_occurred_at");
    }
}

/// <summary>What a game event says a player did (see <see cref="ActivityWriter"/>).</summary>
public sealed record ActivityRecord(
    Guid ActorId,
    ActivityType Type,
    DateTimeOffset OccurredAt,
    Guid? MatchId = null,
    string? MatchName = null,
    string? Outcome = null,
    int? SetsWon = null,
    int? SetsLost = null,
    int? LevelFrom = null,
    int? LevelTo = null);

/// <summary>Records activities, once each. Called by the game-event handlers in Quadra.Api.</summary>
public sealed class ActivityWriter
{
    private readonly ProfileDbContext _context;

    public ActivityWriter(ProfileDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Stores the activity unless the actor already has it (repeated event) or has no profile
    /// (a guest without an account).
    /// </summary>
    public async Task RecordAsync(ActivityRecord a, CancellationToken cancellationToken)
    {
        if (!await _context.PlayerProfiles.AnyAsync(p => p.UserId == a.ActorId, cancellationToken))
        {
            return;
        }

        var key = a.Type == ActivityType.LevelUp ? $"LevelUp:{a.LevelTo}" : $"{a.Type}:{a.MatchId:N}";
        var name = a.MatchName is { Length: > 200 } ? a.MatchName[..200] : a.MatchName;
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO activities
                 (id, actor_id, type, match_id, match_name, outcome, sets_won, sets_lost, level_from, level_to, occurred_at, dedup_key)
             VALUES
                 ({Guid.NewGuid()}, {a.ActorId}, {a.Type.ToString()}, {a.MatchId}, {name}, {a.Outcome}, {a.SetsWon},
                  {a.SetsLost}, {a.LevelFrom}, {a.LevelTo}, {a.OccurredAt.ToUniversalTime()}, {key})
             ON CONFLICT (actor_id, dedup_key) DO NOTHING
             """,
            cancellationToken);
    }
}

/// <summary>Who did it, as the activity feed shows them.</summary>
public sealed record ActivityActorResponse(Guid UserId, string DisplayName, string? Handle, string? PhotoUrl, int Level);

public sealed record ActivityMatchResponse(Guid Id, string Name);

/// <param name="Type"><c>MatchStarted</c>, <c>MatchEnded</c>, <c>MvpAwarded</c> or <c>LevelUp</c>.</param>
/// <param name="OthersCount">For <c>MatchStarted</c>: how many other followed players are in the same game.</param>
/// <param name="Outcome"><c>Win</c>, <c>Loss</c> or <c>Draw</c> for <c>MatchEnded</c>.</param>
public sealed record ActivityResponse(
    Guid Id,
    string Type,
    ActivityActorResponse Actor,
    ActivityMatchResponse? Match,
    int OthersCount,
    string? Outcome,
    int? SetsWon,
    int? SetsLost,
    int? LevelFrom,
    int? LevelTo,
    DateTimeOffset OccurredAt);

/// <param name="NextBefore">Pass it as <c>before</c> for the next page; null on the last one.</param>
public sealed record ActivityFeedResponse(IReadOnlyList<ActivityResponse> Items, DateTimeOffset? NextBefore);

public sealed record ActivitySharingRequest(bool? Enabled);

public sealed record ActivitySharingResponse(bool Enabled);

/// <summary>Reads the friends' activity feed and the caller's sharing switch.</summary>
public sealed class ActivitiesHandler
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;
    public static readonly TimeSpan Retention = TimeSpan.FromDays(30);

    // ponytail: the read filters in memory over at most ScanLimit rows of the last 30 days (some
    // rules need other modules). Plenty while people follow a few dozen players; move the public
    // and live filters into stored columns kept by events if feeds grow past that.
    private const int ScanLimit = 500;

    private readonly ProfileDbContext _context;
    private readonly IMatchAccessReader _matchAccess;
    private readonly IMatchResultReader _matchResults;
    private readonly IProfilePhotoStorage _photoStorage;
    private readonly TimeProvider _timeProvider;

    public ActivitiesHandler(
        ProfileDbContext context,
        IMatchAccessReader matchAccess,
        IMatchResultReader matchResults,
        IProfilePhotoStorage photoStorage,
        TimeProvider timeProvider)
    {
        _context = context;
        _matchAccess = matchAccess;
        _matchResults = matchResults;
        _photoStorage = photoStorage;
        _timeProvider = timeProvider;
    }

    /// <summary>
    /// Activities of the players the caller follows (who share them), newest first, older than
    /// <paramref name="before"/> when given. Activities that happened at the same moment stay on
    /// the same page, so a page can hold a few more than <paramref name="limit"/>.
    /// </summary>
    public async Task<ActivityFeedResponse> ListAsync(
        Guid callerId,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, MaxPageSize);
        var since = _timeProvider.GetUtcNow().Add(-Retention).ToUniversalTime();
        var query = _context.Activities.AsNoTracking()
            .Where(a => a.OccurredAt >= since)
            .Where(a => _context.Follows.Any(f => f.FollowerId == callerId && f.FollowedId == a.ActorId))
            .Where(a => _context.PlayerProfiles.Any(p => p.UserId == a.ActorId && p.ShareActivity));
        if (before is { } cursor)
        {
            var cursorUtc = cursor.ToUniversalTime();
            query = query.Where(a => a.OccurredAt < cursorUtc);
        }

        var rows = await query
            .OrderByDescending(a => a.OccurredAt)
            .ThenBy(a => a.Id)
            .Take(ScanLimit)
            .ToListAsync(cancellationToken);
        var scanFull = rows.Count == ScanLimit;

        // Rules from the other modules: the match must be open now; "started" only while live.
        var matchIds = rows.Where(a => a.MatchId.HasValue).Select(a => a.MatchId!.Value).Distinct().ToArray();
        var open = await _matchAccess.ListPublicAsync(matchIds, cancellationToken);
        var live = new HashSet<Guid>();
        foreach (var id in rows.Where(a => a.Type == ActivityType.MatchStarted && open.Contains(a.MatchId!.Value))
                     .Select(a => a.MatchId!.Value).Distinct())
        {
            var result = await _matchResults.GetMatchResultAsync(id, cancellationToken);
            if (result?.ScoreboardState == "InProgress")
            {
                live.Add(id);
            }
        }

        var visible = rows
            .Where(a => a.MatchId is not { } m || open.Contains(m))
            .Where(a => a.Type != ActivityType.MatchStarted || live.Contains(a.MatchId!.Value))
            .ToList();

        // Several followed players starting the same game read as one line.
        var startedBy = visible.Where(a => a.Type == ActivityType.MatchStarted)
            .GroupBy(a => a.MatchId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(a => a.ActorId).Distinct().Count());
        var shownStarted = new HashSet<Guid>();
        var items = visible
            .Where(a => a.Type != ActivityType.MatchStarted || shownStarted.Add(a.MatchId!.Value))
            .ToList();

        var page = items.Take(take).ToList();
        if (page.Count == take)
        {
            page.AddRange(items.Skip(take).TakeWhile(a => a.OccurredAt == page[^1].OccurredAt));
        }

        DateTimeOffset? nextBefore = page.Count < items.Count ? page[^1].OccurredAt
            : scanFull ? rows[^1].OccurredAt
            : null;
        var actors = await LoadActorsAsync(page.Select(a => a.ActorId), cancellationToken);

        return new ActivityFeedResponse(
            page.Select(a => new ActivityResponse(
                    a.Id,
                    a.Type.ToString(),
                    actors[a.ActorId],
                    a.MatchId is { } m ? new ActivityMatchResponse(m, a.MatchName ?? string.Empty) : null,
                    a.Type == ActivityType.MatchStarted ? startedBy[a.MatchId!.Value] - 1 : 0,
                    a.Outcome,
                    a.SetsWon,
                    a.SetsLost,
                    a.LevelFrom,
                    a.LevelTo,
                    a.OccurredAt))
                .ToList(),
            nextBefore);
    }

    public async Task<ActivitySharingResponse?> SetSharingAsync(Guid callerId, bool enabled, CancellationToken cancellationToken)
    {
        var profile = await _context.PlayerProfiles.FirstOrDefaultAsync(p => p.UserId == callerId, cancellationToken);
        if (profile is null)
        {
            return null;
        }

        profile.SetShareActivity(enabled, _timeProvider.GetUtcNow());
        await _context.SaveChangesAsync(cancellationToken);
        return new ActivitySharingResponse(profile.ShareActivity);
    }

    private async Task<IReadOnlyDictionary<Guid, ActivityActorResponse>> LoadActorsAsync(
        IEnumerable<Guid> actorIds,
        CancellationToken cancellationToken)
    {
        var ids = actorIds.Distinct().ToArray();
        var people = await PostAuthors.LoadAsync(_context, _photoStorage, ids, cancellationToken);
        var stats = await _context.PlayerStats.AsNoTracking()
            .Where(s => ids.Contains(s.UserId))
            .ToDictionaryAsync(s => s.UserId, cancellationToken);

        return ids.ToDictionary(
            id => id,
            id => new ActivityActorResponse(
                id,
                people[id].DisplayName,
                people[id].Handle,
                people[id].PhotoUrl,
                stats.TryGetValue(id, out var s) ? PlayerProgression.LevelOf(s) : 1));
    }
}

[ApiController]
[Authorize]
public sealed class ActivitiesController : ControllerBase
{
    private readonly ActivitiesHandler _handler;

    public ActivitiesController(ActivitiesHandler handler)
    {
        _handler = handler;
    }

    /// <summary>
    /// GET /api/v1/activities — what the players the caller follows did, newest first
    /// (<c>limit</c> 20 by default, at most 50; <c>before</c> = the previous page's <c>nextBefore</c>).
    /// </summary>
    [HttpGet("api/v1/activities")]
    public async Task<ActionResult<ActivityFeedResponse>> List(
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int limit = ActivitiesHandler.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return Ok(await _handler.ListAsync(callerId, before, limit, cancellationToken));
    }

    /// <summary>
    /// PUT /api/v1/profiles/me/activity-sharing — "Mostrar minhas atividades para quem me segue".
    /// Off hides every activity of the caller, including those already recorded.
    /// </summary>
    [HttpPut("api/v1/profiles/me/activity-sharing")]
    public async Task<ActionResult<ActivitySharingResponse>> SetSharing(
        [FromBody] ActivitySharingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        if (request.Enabled is not { } enabled)
        {
            ModelState.AddModelError(nameof(request.Enabled), "Enabled is required.");
            return ValidationProblem(ModelState);
        }

        var result = await _handler.SetSharingAsync(callerId, enabled, cancellationToken);
        return result is null ? NotFound() : Ok(result);
    }

    private bool TryGetCallerId(out Guid callerId) =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out callerId);
}
