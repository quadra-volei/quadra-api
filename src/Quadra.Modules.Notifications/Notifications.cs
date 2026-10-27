using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Notifications;

// Essential notifications (DECISIONS #77): an in-app list with an unread counter, and a push to
// the phone through Expo Push for the categories the player left on. Other modules ask for one
// through INotificationSender; nobody else reads these tables.

/// <summary>One notification of one user; at most one per user and dedup key.</summary>
public sealed class Notification
{
    public Guid Id { get; init; }

    public Guid UserId { get; init; }

    public string Type { get; init; } = null!;

    public string Title { get; init; } = null!;

    public string Body { get; init; } = null!;

    public Guid? MatchId { get; init; }

    public string DedupKey { get; init; } = null!;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? ReadAt { get; set; }

    /// <summary>When the user cleared it from the list. Kept, not deleted, so its dedup key still
    /// stops a repeated trigger (the reminder sweep) from sending it again.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>An Expo push token of one of the user's phones.</summary>
public sealed class PushToken
{
    public string Token { get; init; } = null!;

    public Guid UserId { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>The app's three switches (Configurações → Notificações); no row = all on.</summary>
public sealed class NotificationPreferences
{
    public Guid UserId { get; init; }

    public bool Invites { get; set; } = true;

    public bool Reminders { get; set; } = true;

    public bool Ranking { get; set; } = true;

    public bool Allows(NotificationCategory category) => category switch
    {
        NotificationCategory.Invites => Invites,
        NotificationCategory.Reminders => Reminders,
        _ => Ranking,
    };
}

public sealed class NotificationsDbContext : DbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
        : base(options)
    {
    }

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<PushToken> PushTokens => Set<PushToken>();

    public DbSet<NotificationPreferences> Preferences => Set<NotificationPreferences>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        var n = modelBuilder.Entity<Notification>();
        n.ToTable("notifications");
        n.HasKey(x => x.Id);
        n.Property(x => x.Id).HasColumnName("id");
        n.Property(x => x.UserId).HasColumnName("user_id");
        n.Property(x => x.Type).HasColumnName("type").HasMaxLength(40).IsRequired();
        n.Property(x => x.Title).HasColumnName("title").HasMaxLength(120).IsRequired();
        n.Property(x => x.Body).HasColumnName("body").HasMaxLength(300).IsRequired();
        n.Property(x => x.MatchId).HasColumnName("match_id");
        n.Property(x => x.DedupKey).HasColumnName("dedup_key").HasMaxLength(120).IsRequired();
        n.Property(x => x.CreatedAt).HasColumnName("created_at").IsRequired();
        n.Property(x => x.ReadAt).HasColumnName("read_at");
        n.Property(x => x.DeletedAt).HasColumnName("deleted_at");
        n.HasIndex(x => new { x.UserId, x.DedupKey }).IsUnique().HasDatabaseName("ux_notifications_user_dedup");
        n.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_notifications_user_created");

        var t = modelBuilder.Entity<PushToken>();
        t.ToTable("push_tokens");
        t.HasKey(x => x.Token);
        t.Property(x => x.Token).HasColumnName("token").HasMaxLength(200);
        t.Property(x => x.UserId).HasColumnName("user_id");
        t.Property(x => x.UpdatedAt).HasColumnName("updated_at").IsRequired();
        t.HasIndex(x => x.UserId).HasDatabaseName("ix_push_tokens_user_id");

        var p = modelBuilder.Entity<NotificationPreferences>();
        p.ToTable("notification_preferences");
        p.HasKey(x => x.UserId);
        p.Property(x => x.UserId).HasColumnName("user_id");
        p.Property(x => x.Invites).HasColumnName("invites");
        p.Property(x => x.Reminders).HasColumnName("reminders");
        p.Property(x => x.Ranking).HasColumnName("ranking");
    }
}

public sealed class NotificationsDbContextDesignTimeFactory : IDesignTimeDbContextFactory<NotificationsDbContext>
{
    public NotificationsDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                ?? "Host=localhost;Port=5432;Database=quadra;Username=quadra;Password=quadra")
            .Options);
}

// ── Contracts ──────────────────────────────────────────────────────────────

public sealed record NotificationResponse(
    Guid Id,
    string Type,
    string Title,
    string Body,
    Guid? MatchId,
    DateTimeOffset CreatedAt,
    bool Read);

public sealed record NotificationPageResponse(IReadOnlyList<NotificationResponse> Items, DateTimeOffset? NextBefore);

public sealed record UnreadCountResponse(int Count);

public sealed record PushTokenRequest(string? Token);

public sealed record NotificationPreferencesDto(bool Invites, bool Reminders, bool Ranking);

// ── Push ───────────────────────────────────────────────────────────────────

/// <summary>Sends pushes through Expo Push (the app registers an Expo push token).</summary>
public sealed class ExpoPushClient
{
    public const string HttpClientName = "expo-push";
    private const string Endpoint = "https://exp.host/--/api/v2/push/send";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<ExpoPushClient> _logger;

    public ExpoPushClient(IHttpClientFactory httpClientFactory, ILogger<ExpoPushClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>Best effort: a failed push is logged, never thrown (the in-app list has it anyway).</summary>
    public async Task SendAsync(IReadOnlyCollection<string> tokens, NotificationRequest notification, CancellationToken cancellationToken)
    {
        if (tokens.Count == 0)
        {
            return;
        }

        // Expo takes up to 100 messages per request.
        foreach (var batch in tokens.Chunk(100))
        {
            var messages = batch.Select(token => new
            {
                to = token,
                title = notification.Title,
                body = notification.Body,
                sound = "default",
                data = new { type = notification.Type, matchId = notification.MatchId },
            });
            try
            {
                using var response = await _httpClientFactory.CreateClient(HttpClientName)
                    .PostAsJsonAsync(Endpoint, messages, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Expo push answered {Status} for {Count} messages.", (int)response.StatusCode, batch.Length);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning(ex, "Expo push failed for {Count} messages.", batch.Length);
            }
        }
    }
}

// ── Sending ────────────────────────────────────────────────────────────────

public sealed class NotificationSender : INotificationSender
{
    private readonly NotificationsDbContext _db;
    private readonly ExpoPushClient _push;
    private readonly TimeProvider _timeProvider;

    public NotificationSender(NotificationsDbContext db, ExpoPushClient push, TimeProvider timeProvider)
    {
        _db = db;
        _push = push;
        _timeProvider = timeProvider;
    }

    public async Task SendAsync(IReadOnlyCollection<Guid> userIds, NotificationRequest notification, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var fresh = new List<Guid>();
        foreach (var userId in userIds.Distinct())
        {
            // Once per user and dedup key: a repeated trigger (a second sweep) changes nothing.
            var inserted = await _db.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO notifications (id, user_id, type, title, body, match_id, dedup_key, created_at) VALUES ({Guid.NewGuid()}, {userId}, {notification.Type}, {notification.Title}, {notification.Body}, {notification.MatchId}, {notification.DedupKey}, {now}) ON CONFLICT (user_id, dedup_key) DO NOTHING",
                cancellationToken);
            if (inserted > 0)
            {
                fresh.Add(userId);
            }
        }

        if (fresh.Count == 0)
        {
            return;
        }

        var muted = await _db.Preferences.AsNoTracking().Where(p => fresh.Contains(p.UserId)).ToListAsync(cancellationToken);
        var recipients = fresh.Where(id => muted.FirstOrDefault(p => p.UserId == id)?.Allows(notification.Category) ?? true).ToArray();
        var tokens = await _db.PushTokens.AsNoTracking()
            .Where(t => recipients.Contains(t.UserId))
            .Select(t => t.Token)
            .ToListAsync(cancellationToken);
        await _push.SendAsync(tokens, notification, cancellationToken);
    }
}

// ── Reading ────────────────────────────────────────────────────────────────

public sealed class NotificationsHandler
{
    public const int DefaultPageSize = 30;
    public const int MaxPageSize = 50;

    private readonly NotificationsDbContext _db;
    private readonly TimeProvider _timeProvider;

    public NotificationsHandler(NotificationsDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<NotificationPageResponse> ListAsync(Guid userId, DateTimeOffset? before, int limit, CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, MaxPageSize);
        var query = _db.Notifications.AsNoTracking().Where(n => n.UserId == userId && n.DeletedAt == null);
        if (before is { } cursor)
        {
            var cursorUtc = cursor.ToUniversalTime();
            query = query.Where(n => n.CreatedAt < cursorUtc);
        }

        var rows = await query.OrderByDescending(n => n.CreatedAt).Take(take + 1).ToListAsync(cancellationToken);
        var hasMore = rows.Count > take;
        if (hasMore)
        {
            rows.RemoveAt(take);
        }

        return new NotificationPageResponse(
            rows.Select(n => new NotificationResponse(n.Id, n.Type, n.Title, n.Body, n.MatchId, n.CreatedAt, n.ReadAt is not null)).ToList(),
            hasMore ? rows[^1].CreatedAt : null);
    }

    public async Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken) =>
        await _db.Notifications.CountAsync(n => n.UserId == userId && n.ReadAt == null && n.DeletedAt == null, cancellationToken);

    /// <returns>false when the user has no such notification.</returns>
    public async Task<bool> MarkReadAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        if (!await _db.Notifications.AnyAsync(n => n.Id == id && n.UserId == userId && n.DeletedAt == null, cancellationToken))
        {
            return false;
        }

        await _db.Notifications
            .Where(n => n.Id == id && n.UserId == userId && n.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, (DateTimeOffset?)now), cancellationToken);
        return true;
    }

    public Task MarkAllReadAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return _db.Notifications
            .Where(n => n.UserId == userId && n.ReadAt == null && n.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, (DateTimeOffset?)now), cancellationToken);
    }

    /// <returns>false when the user has no such notification (or already cleared it).</returns>
    public async Task<bool> DeleteAsync(Guid userId, Guid id, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var cleared = await _db.Notifications
            .Where(n => n.Id == id && n.UserId == userId && n.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.DeletedAt, (DateTimeOffset?)now), cancellationToken);
        return cleared > 0;
    }

    public Task DeleteAllAsync(Guid userId, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return _db.Notifications
            .Where(n => n.UserId == userId && n.DeletedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.DeletedAt, (DateTimeOffset?)now), cancellationToken);
    }

    /// <summary>A token belongs to one account: the phone's last login takes it.</summary>
    public async Task SetPushTokenAsync(Guid userId, string token, CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO push_tokens (token, user_id, updated_at) VALUES ({token}, {userId}, {now}) ON CONFLICT (token) DO UPDATE SET user_id = EXCLUDED.user_id, updated_at = EXCLUDED.updated_at",
            cancellationToken);
    }

    public Task RemovePushTokenAsync(Guid userId, string token, CancellationToken cancellationToken) =>
        _db.PushTokens.Where(t => t.Token == token && t.UserId == userId).ExecuteDeleteAsync(cancellationToken);

    public async Task<NotificationPreferencesDto> GetPreferencesAsync(Guid userId, CancellationToken cancellationToken)
    {
        var p = await _db.Preferences.AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken)
            ?? new NotificationPreferences { UserId = userId };
        return new NotificationPreferencesDto(p.Invites, p.Reminders, p.Ranking);
    }

    public async Task<NotificationPreferencesDto> SetPreferencesAsync(Guid userId, NotificationPreferencesDto dto, CancellationToken cancellationToken)
    {
        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO notification_preferences (user_id, invites, reminders, ranking) VALUES ({userId}, {dto.Invites}, {dto.Reminders}, {dto.Ranking}) ON CONFLICT (user_id) DO UPDATE SET invites = EXCLUDED.invites, reminders = EXCLUDED.reminders, ranking = EXCLUDED.ranking",
            cancellationToken);
        return dto;
    }
}

/// <summary>Notifications part of account export and deletion (SEC-14).</summary>
public sealed class NotificationsPersonalData : IPersonalDataStore
{
    private readonly NotificationsDbContext _db;

    public NotificationsPersonalData(NotificationsDbContext db)
    {
        _db = db;
    }

    public string Section => "notifications";

    public async Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken) => new
    {
        Notifications = await _db.Notifications.AsNoTracking().Where(n => n.UserId == userId).ToListAsync(cancellationToken),
        Preferences = await _db.Preferences.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken),
        PushTokens = await _db.PushTokens.AsNoTracking().CountAsync(t => t.UserId == userId, cancellationToken),
    };

    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        await _db.Notifications.Where(n => n.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.PushTokens.Where(t => t.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.Preferences.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
    }
}

// ── HTTP ───────────────────────────────────────────────────────────────────

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
public sealed class NotificationsController : ControllerBase
{
    private readonly NotificationsHandler _handler;

    public NotificationsController(NotificationsHandler handler)
    {
        _handler = handler;
    }

    /// <summary>GET /api/v1/notifications — newest first, older than <c>before</c> when given.</summary>
    [HttpGet]
    public async Task<ActionResult<NotificationPageResponse>> List(
        [FromQuery] DateTimeOffset? before,
        CancellationToken cancellationToken,
        [FromQuery] int limit = NotificationsHandler.DefaultPageSize) =>
        TryGetCallerId(out var userId) ? Ok(await _handler.ListAsync(userId, before, limit, cancellationToken)) : Unauthorized();

    /// <summary>GET /api/v1/notifications/unread-count — the bell's badge.</summary>
    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountResponse>> UnreadCount(CancellationToken cancellationToken) =>
        TryGetCallerId(out var userId) ? Ok(new UnreadCountResponse(await _handler.CountUnreadAsync(userId, cancellationToken))) : Unauthorized();

    /// <summary>POST /api/v1/notifications/{id}/read</summary>
    [HttpPost("{id:guid}/read")]
    public async Task<IActionResult> MarkRead(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        return await _handler.MarkReadAsync(userId, id, cancellationToken) ? NoContent() : NotFound();
    }

    /// <summary>POST /api/v1/notifications/read-all</summary>
    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        await _handler.MarkAllReadAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>DELETE /api/v1/notifications/{id} — clears one from the caller's list.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        return await _handler.DeleteAsync(userId, id, cancellationToken) ? NoContent() : NotFound();
    }

    /// <summary>DELETE /api/v1/notifications — clears the caller's whole list.</summary>
    [HttpDelete]
    public async Task<IActionResult> DeleteAll(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        await _handler.DeleteAllAsync(userId, cancellationToken);
        return NoContent();
    }

    /// <summary>PUT /api/v1/notifications/push-token — this phone's Expo push token.</summary>
    [HttpPut("push-token")]
    public async Task<IActionResult> SetPushToken([FromBody] PushTokenRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        if (!IsExpoToken(request.Token))
        {
            ModelState.AddModelError(nameof(request.Token), "token must be an Expo push token (ExponentPushToken[…]).");
            return ValidationProblem(ModelState);
        }

        await _handler.SetPushTokenAsync(userId, request.Token!, cancellationToken);
        return NoContent();
    }

    /// <summary>DELETE /api/v1/notifications/push-token — stop pushing to this phone (logout).</summary>
    [HttpDelete("push-token")]
    public async Task<IActionResult> RemovePushToken([FromBody] PushTokenRequest request, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var userId))
        {
            return Unauthorized();
        }

        if (IsExpoToken(request.Token))
        {
            await _handler.RemovePushTokenAsync(userId, request.Token!, cancellationToken);
        }

        return NoContent();
    }

    /// <summary>GET /api/v1/notifications/preferences</summary>
    [HttpGet("preferences")]
    public async Task<ActionResult<NotificationPreferencesDto>> GetPreferences(CancellationToken cancellationToken) =>
        TryGetCallerId(out var userId) ? Ok(await _handler.GetPreferencesAsync(userId, cancellationToken)) : Unauthorized();

    /// <summary>PUT /api/v1/notifications/preferences — the app's three switches.</summary>
    [HttpPut("preferences")]
    public async Task<ActionResult<NotificationPreferencesDto>> SetPreferences(
        [FromBody] NotificationPreferencesDto request,
        CancellationToken cancellationToken) =>
        TryGetCallerId(out var userId) ? Ok(await _handler.SetPreferencesAsync(userId, request, cancellationToken)) : Unauthorized();

    private static bool IsExpoToken(string? token) =>
        token is { Length: <= 200 }
        && (token.StartsWith("ExponentPushToken[", StringComparison.Ordinal) || token.StartsWith("ExpoPushToken[", StringComparison.Ordinal))
        && token.EndsWith(']');

    private bool TryGetCallerId(out Guid callerId) =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out callerId);
}

// ── Composition ────────────────────────────────────────────────────────────

public static class NotificationsModuleExtensions
{
    public static IServiceCollection AddNotificationsModule(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddDbContext<NotificationsDbContext>((sp, builder) =>
        {
            var cfg = sp.GetRequiredService<IConfiguration>();
            var connectionString = cfg.GetConnectionString("Notifications")
                ?? cfg.GetConnectionString("Default")
                ?? throw new InvalidOperationException(
                    "ConnectionStrings:Notifications (or ConnectionStrings:Default) must be configured.");
            builder.UseNpgsql(PostgresConnectionString.Normalize(connectionString));
        });

        services.AddHttpClient(ExpoPushClient.HttpClientName)
            .ConfigureHttpClient(static client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddSingleton<ExpoPushClient>();
        services.AddScoped<INotificationSender, NotificationSender>();
        services.AddScoped<NotificationsHandler>();
        services.AddScoped<IPersonalDataStore, NotificationsPersonalData>();
        return services;
    }
}
