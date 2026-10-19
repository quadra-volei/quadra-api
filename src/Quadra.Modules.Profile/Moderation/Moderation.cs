using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;
using Quadra.Modules.Profile.Posts;

namespace Quadra.Modules.Profile.Moderation;

// Safety of the Rede (app store rules for user content): a player reports a post or blocks
// another player; the team (admins listed in Moderation:AdminUserIds) reviews the reports and
// deletes posts. A block hides each one's posts and comments from the other, removes the follows
// between them and stops new follows, likes and comments between them. Matches are not affected.

/// <summary>One player blocking another; at most one row per pair.</summary>
public sealed class Block
{
    public Guid BlockerId { get; init; }

    public Guid BlockedId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class BlockConfiguration : IEntityTypeConfiguration<Block>
{
    public void Configure(EntityTypeBuilder<Block> builder)
    {
        builder.ToTable("blocks");

        builder.HasKey(b => new { b.BlockerId, b.BlockedId });
        builder.Property(b => b.BlockerId).HasColumnName("blocker_id");
        builder.Property(b => b.BlockedId).HasColumnName("blocked_id");
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        // The key serves "who did I block"; this one serves "who blocked me".
        builder.HasIndex(b => b.BlockedId).HasDatabaseName("ix_blocks_blocked_id");
    }
}

public enum ReportReason
{
    Spam,
    Offensive,
    Inappropriate,
    Other,
}

/// <summary>A player's report of a post; one per reporter and post. Deleted with the post.</summary>
public sealed class PostReport
{
    public Guid Id { get; init; }

    public Guid PostId { get; init; }

    public Guid ReporterId { get; init; }

    public ReportReason Reason { get; init; }

    public string? Details { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class PostReportConfiguration : IEntityTypeConfiguration<PostReport>
{
    public void Configure(EntityTypeBuilder<PostReport> builder)
    {
        builder.ToTable("post_reports");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.PostId).HasColumnName("post_id");
        builder.Property(r => r.ReporterId).HasColumnName("reporter_id");
        builder.Property(r => r.Reason).HasColumnName("reason").HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.Details).HasColumnName("details").HasMaxLength(ReportPostRequestValidator.MaxDetailsLength);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").IsRequired();

        builder.HasIndex(r => new { r.PostId, r.ReporterId }).IsUnique().HasDatabaseName("ux_post_reports_post_reporter");
        builder.HasOne<Post>().WithMany().HasForeignKey(r => r.PostId).OnDelete(DeleteBehavior.Cascade);
    }
}

public sealed record ReportPostRequest(string? Reason, string? Details);

public sealed class ReportPostRequestValidator : AbstractValidator<ReportPostRequest>
{
    public const int MaxDetailsLength = 500;

    public ReportPostRequestValidator()
    {
        RuleFor(r => r.Reason)
            .NotEmpty()
            .IsEnumName(typeof(ReportReason), caseSensitive: false);
        RuleFor(r => r.Details).MaximumLength(MaxDetailsLength);
    }
}

/// <summary>A player the caller blocked, for the list where they unblock.</summary>
public sealed record BlockedPlayerResponse(Guid UserId, string DisplayName, string? Handle, DateTimeOffset BlockedAt);

/// <summary>A reported post waiting for the team, with every report it got.</summary>
public sealed record ReportedPostResponse(
    Guid PostId,
    Guid AuthorId,
    string AuthorName,
    string? Text,
    bool HasPhoto,
    DateTimeOffset PostedAt,
    int ReportCount,
    IReadOnlyList<ReportResponse> Reports);

public sealed record ReportResponse(Guid ReporterId, string Reason, string? Details, DateTimeOffset ReportedAt);

/// <summary>Admins of the moderation routes, by account id (Render: Moderation__AdminUserIds__0 …).</summary>
public sealed class ModerationOptions
{
    public const string SectionName = "Moderation";
    public const string AdminPolicy = "Admin";

    public Guid[] AdminUserIds { get; set; } = [];
}

/// <summary>Blocks between two players, in either direction.</summary>
internal static class BlockRules
{
    public static Task<bool> AnyBetweenAsync(this DbSet<Block> blocks, Guid a, Guid b, CancellationToken cancellationToken) =>
        blocks.AnyAsync(
            x => (x.BlockerId == a && x.BlockedId == b) || (x.BlockerId == b && x.BlockedId == a),
            cancellationToken);
}

public sealed class ModerationHandler
{
    private readonly ProfileDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ModerationHandler(ProfileDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    /// <summary>Blocks or unblocks; repeating either changes nothing. Blocking ends the follows both ways.</summary>
    /// <returns>false when there is no such player.</returns>
    public async Task<bool> SetBlockAsync(Guid callerId, Guid userId, bool blocked, CancellationToken cancellationToken)
    {
        if (!await _context.PlayerProfiles.AnyAsync(p => p.UserId == userId, cancellationToken))
        {
            return false;
        }

        if (!blocked)
        {
            await _context.Blocks
                .Where(b => b.BlockerId == callerId && b.BlockedId == userId)
                .ExecuteDeleteAsync(cancellationToken);
            return true;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);
        // ON CONFLICT keeps a double tap (two requests at once) from failing on the key.
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO blocks (blocker_id, blocked_id) VALUES ({callerId}, {userId}) ON CONFLICT DO NOTHING",
            cancellationToken);
        await _context.Follows
            .Where(f => (f.FollowerId == callerId && f.FollowedId == userId) || (f.FollowerId == userId && f.FollowedId == callerId))
            .ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<BlockedPlayerResponse>> ListBlockedAsync(Guid callerId, CancellationToken cancellationToken)
    {
        var blocks = await _context.Blocks.AsNoTracking()
            .Where(b => b.BlockerId == callerId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);
        var ids = blocks.Select(b => b.BlockedId).ToArray();
        var profiles = await _context.PlayerProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        return blocks
            .Select(b => new BlockedPlayerResponse(
                b.BlockedId,
                profiles.GetValueOrDefault(b.BlockedId)?.DisplayName ?? PlayerProfile.PlaceholderDisplayName,
                profiles.GetValueOrDefault(b.BlockedId)?.Handle,
                b.CreatedAt))
            .ToList();
    }

    /// <summary>Reports a post (once per player; a repeat changes nothing). The post leaves the reporter's feed.</summary>
    /// <returns>false when there is no such post, or a block hides it from the caller.</returns>
    public async Task<bool> ReportAsync(Guid callerId, Guid postId, ReportPostRequest request, CancellationToken cancellationToken)
    {
        var authorId = await _context.Posts.AsNoTracking()
            .Where(p => p.Id == postId)
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(cancellationToken);
        if (authorId is null || await _context.Blocks.AnyBetweenAsync(callerId, authorId.Value, cancellationToken))
        {
            return false;
        }

        var reason = Enum.Parse<ReportReason>(request.Reason!, ignoreCase: true).ToString();
        var details = string.IsNullOrWhiteSpace(request.Details) ? null : request.Details.Trim();
        var now = _timeProvider.GetUtcNow();
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"INSERT INTO post_reports (id, post_id, reporter_id, reason, details, created_at) VALUES ({Guid.NewGuid()}, {postId}, {callerId}, {reason}, {details}, {now}) ON CONFLICT (post_id, reporter_id) DO NOTHING",
            cancellationToken);
        return true;
    }

    // ponytail: every open report in one page, oldest first. Page it when the queue grows.
    public async Task<IReadOnlyList<ReportedPostResponse>> ListReportsAsync(CancellationToken cancellationToken)
    {
        var reports = await _context.PostReports.AsNoTracking().OrderBy(r => r.CreatedAt).ToListAsync(cancellationToken);
        var postIds = reports.Select(r => r.PostId).Distinct().ToArray();
        var posts = await _context.Posts.AsNoTracking()
            .Where(p => postIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, cancellationToken);
        var authorIds = posts.Values.Select(p => p.AuthorId).Distinct().ToArray();
        var authors = await _context.PlayerProfiles.AsNoTracking()
            .Where(p => authorIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        return postIds
            .Select(id => posts[id])
            .Select(post =>
            {
                var postReports = reports.Where(r => r.PostId == post.Id).ToList();
                return new ReportedPostResponse(
                    post.Id,
                    post.AuthorId,
                    authors.GetValueOrDefault(post.AuthorId)?.DisplayName ?? PlayerProfile.PlaceholderDisplayName,
                    post.Text,
                    post.PhotoObjectKey is not null,
                    post.CreatedAt,
                    postReports.Count,
                    postReports.Select(r => new ReportResponse(r.ReporterId, r.Reason.ToString(), r.Details, r.CreatedAt)).ToList());
            })
            .ToList();
    }

    /// <summary>The team deletes a post; its reports, likes and comments go with it.</summary>
    /// <returns>false when there is no such post.</returns>
    public async Task<bool> DeletePostAsync(Guid postId, CancellationToken cancellationToken) =>
        await _context.Posts.Where(p => p.Id == postId).ExecuteDeleteAsync(cancellationToken) > 0;

    /// <summary>The team keeps a reported post: its reports are cleared.</summary>
    /// <returns>false when the post had no reports.</returns>
    public async Task<bool> DismissReportsAsync(Guid postId, CancellationToken cancellationToken) =>
        await _context.PostReports.Where(r => r.PostId == postId).ExecuteDeleteAsync(cancellationToken) > 0;
}

[ApiController]
[Authorize]
public sealed class ModerationController : ControllerBase
{
    private readonly ModerationHandler _handler;
    private readonly IValidator<ReportPostRequest> _reportValidator;

    public ModerationController(ModerationHandler handler, IValidator<ReportPostRequest> reportValidator)
    {
        _handler = handler;
        _reportValidator = reportValidator;
    }

    /// <summary>PUT /api/v1/profiles/{userId}/block — the signed-in user blocks the player.</summary>
    [HttpPut("api/v1/profiles/{userId:guid}/block")]
    public Task<IActionResult> Block(Guid userId, CancellationToken cancellationToken) =>
        SetBlockAsync(userId, blocked: true, cancellationToken);

    /// <summary>DELETE /api/v1/profiles/{userId}/block — unblocks the player.</summary>
    [HttpDelete("api/v1/profiles/{userId:guid}/block")]
    public Task<IActionResult> Unblock(Guid userId, CancellationToken cancellationToken) =>
        SetBlockAsync(userId, blocked: false, cancellationToken);

    /// <summary>GET /api/v1/profiles/me/blocks — the players the signed-in user blocked.</summary>
    [HttpGet("api/v1/profiles/me/blocks")]
    public async Task<ActionResult<IReadOnlyList<BlockedPlayerResponse>>> ListBlocked(CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return Ok(await _handler.ListBlockedAsync(callerId, cancellationToken));
    }

    /// <summary>POST /api/v1/posts/{postId}/report — reports a post to the team.</summary>
    [HttpPost("api/v1/posts/{postId:guid}/report")]
    public async Task<IActionResult> Report(
        Guid postId,
        [FromBody] ReportPostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _reportValidator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var failure in validation.Errors)
            {
                ModelState.AddModelError(failure.PropertyName, failure.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        return await _handler.ReportAsync(callerId, postId, request, cancellationToken) ? NoContent() : NotFound();
    }

    /// <summary>GET /api/v1/admin/reports — reported posts waiting for the team (admins only).</summary>
    [HttpGet("api/v1/admin/reports")]
    [Authorize(Policy = ModerationOptions.AdminPolicy)]
    public async Task<ActionResult<IReadOnlyList<ReportedPostResponse>>> ListReports(CancellationToken cancellationToken) =>
        Ok(await _handler.ListReportsAsync(cancellationToken));

    /// <summary>DELETE /api/v1/admin/posts/{postId} — the team deletes a post (admins only).</summary>
    [HttpDelete("api/v1/admin/posts/{postId:guid}")]
    [Authorize(Policy = ModerationOptions.AdminPolicy)]
    public async Task<IActionResult> DeletePost(Guid postId, CancellationToken cancellationToken) =>
        await _handler.DeletePostAsync(postId, cancellationToken) ? NoContent() : NotFound();

    /// <summary>DELETE /api/v1/admin/reports/{postId} — the team keeps the post and clears its reports (admins only).</summary>
    [HttpDelete("api/v1/admin/reports/{postId:guid}")]
    [Authorize(Policy = ModerationOptions.AdminPolicy)]
    public async Task<IActionResult> DismissReports(Guid postId, CancellationToken cancellationToken) =>
        await _handler.DismissReportsAsync(postId, cancellationToken) ? NoContent() : NotFound();

    private async Task<IActionResult> SetBlockAsync(Guid userId, bool blocked, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        if (callerId == userId)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: "A player cannot block themselves.");
        }

        return await _handler.SetBlockAsync(callerId, userId, blocked, cancellationToken) ? NoContent() : NotFound();
    }

    private bool TryGetCallerId(out Guid callerId) =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out callerId);
}
