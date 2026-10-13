using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Profile.Posts;

// The whole first version of the "Rede" feed: a player publishes a text and/or one photo,
// optionally tied to a match they played or to one still to come (an invite), and everyone
// reads the feed newest first: everyone's posts ("Para você") or only the posts of the players
// the reader follows ("Amigos", see Follows/Follows.cs).
// A post can be liked and commented on (Comments.cs). An organizer can also share their match
// with no text: the "created the match" card. Nothing is posted automatically.

/// <summary>What a post is: written by a player, or a match card with no text.</summary>
public enum PostKind
{
    /// <summary>Written by the player: text and/or photo.</summary>
    Post,

    /// <summary>
    /// "Created the match": no text, the match it is about. Written when the organizer shares
    /// the match to the feed.
    /// </summary>
    MatchCreated,

    /// <summary>
    /// "Confirmed presence in the match". No longer written: it was automatic for one day
    /// (2026-10-13). Kept so the rows from that day still read.
    /// </summary>
    PresenceConfirmed,
}

/// <summary>One post. Rows are inserted and deleted, never edited.</summary>
public sealed class Post
{
    public const int TextMaxLength = 500;
    public const int PhotoObjectKeyMaxLength = 256;
    public const int MatchNameMaxLength = 200;

    // Private parameterless constructor required by EF Core.
    private Post() { }

    public Guid Id { get; private set; }

    public Guid AuthorId { get; private set; }

    public PostKind Kind { get; private set; }

    public string? Text { get; private set; }

    public string? PhotoObjectKey { get; private set; }

    /// <summary>App-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid? MatchId { get; private set; }

    /// <summary>The match's name when the post was written (snapshot).</summary>
    public string? MatchName { get; private set; }

    /// <summary>
    /// When the match is scheduled (snapshot). Still in the future on a post that invites to
    /// the match. Null on posts written before it was tracked.
    /// </summary>
    public DateTimeOffset? MatchStartsAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Post Create(
        Guid authorId,
        string? text,
        string? photoObjectKey,
        Guid? matchId,
        string? matchName,
        DateTimeOffset? matchStartsAt,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            PhotoObjectKey = string.IsNullOrWhiteSpace(photoObjectKey) ? null : photoObjectKey,
            MatchId = matchId,
            MatchName = matchName,
            MatchStartsAt = matchStartsAt,
            CreatedAt = now,
        };

    /// <summary>The organizer's "created the match" card: no text, the match it is about.</summary>
    public static Post CreateMatchShare(
        Guid authorId,
        Guid matchId,
        string matchName,
        DateTimeOffset matchStartsAt,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            Kind = PostKind.MatchCreated,
            MatchId = matchId,
            MatchName = matchName,
            MatchStartsAt = matchStartsAt,
            CreatedAt = now,
        };
}

public sealed class PostConfiguration : IEntityTypeConfiguration<Post>
{
    public void Configure(EntityTypeBuilder<Post> builder)
    {
        builder.ToTable("posts");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.AuthorId).HasColumnName("author_id").IsRequired();
        builder.Property(p => p.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(24)
            .HasDefaultValue(PostKind.Post).IsRequired();
        builder.Property(p => p.Text).HasColumnName("text").HasMaxLength(Post.TextMaxLength);
        builder.Property(p => p.PhotoObjectKey).HasColumnName("photo_object_key").HasMaxLength(Post.PhotoObjectKeyMaxLength);
        builder.Property(p => p.MatchId).HasColumnName("match_id");
        builder.Property(p => p.MatchName).HasColumnName("match_name").HasMaxLength(Post.MatchNameMaxLength);
        builder.Property(p => p.MatchStartsAt).HasColumnName("match_starts_at");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        // The feed reads newest first; the daily limit counts one author's recent posts; the
        // match index is for listing a match's posts.
        builder.HasIndex(p => p.CreatedAt).HasDatabaseName("ix_posts_created_at");
        builder.HasIndex(p => new { p.AuthorId, p.CreatedAt }).HasDatabaseName("ix_posts_author_id_created_at");
        builder.HasIndex(p => p.MatchId).HasDatabaseName("ix_posts_match_id");
    }
}

/// <summary>One player liking one post; at most one row per pair.</summary>
public sealed class PostLike
{
    public Guid PostId { get; init; }

    public Guid UserId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class PostLikeConfiguration : IEntityTypeConfiguration<PostLike>
{
    public void Configure(EntityTypeBuilder<PostLike> builder)
    {
        builder.ToTable("post_likes");

        builder.HasKey(l => new { l.PostId, l.UserId });
        builder.Property(l => l.PostId).HasColumnName("post_id");
        builder.Property(l => l.UserId).HasColumnName("user_id");
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        // Same module, so a real FK: deleting a post takes its likes with it.
        builder.HasOne<Post>().WithMany().HasForeignKey(l => l.PostId).OnDelete(DeleteBehavior.Cascade);
    }
}

/// <summary>
/// Body of <c>POST /api/v1/posts</c>. A text and/or a photo; or only <c>MatchId</c>, by the
/// organizer of an open match still to come, to share the match itself.
/// </summary>
/// <param name="Text">Up to 500 characters after trimming.</param>
/// <param name="PhotoObjectKey">
/// The <c>objectKey</c> returned by <c>POST /api/v1/profiles/me/photo/upload-url</c>, after the
/// app uploaded the photo to it.
/// </param>
/// <param name="MatchId">
/// A finished match from the author's own history, or an open match still to come that the
/// author organizes or confirmed presence in (the post then invites to it).
/// </param>
public sealed record CreatePostRequest(string? Text, string? PhotoObjectKey = null, Guid? MatchId = null);

public sealed record PostAuthorResponse(Guid UserId, string DisplayName, string? Handle, string? PhotoUrl);

/// <param name="StartsAt">When the match is scheduled; in the future on an invite.</param>
public sealed record PostMatchResponse(Guid Id, string Name, DateTimeOffset? StartsAt);

/// <param name="Kind">
/// <c>Post</c> for a post a player wrote; <c>MatchCreated</c> for a match its organizer
/// shared (no text or photo, always a match). <c>PresenceConfirmed</c> only on old rows.
/// </param>
public sealed record PostResponse(
    Guid Id,
    PostAuthorResponse Author,
    string? Text,
    string? PhotoUrl,
    PostMatchResponse? Match,
    DateTimeOffset CreatedAt,
    bool Mine,
    int LikeCount,
    bool LikedByMe,
    int CommentCount,
    string Kind);

/// <summary>Answer of <c>PUT / DELETE /api/v1/posts/{id}/like</c>: the post's likes afterwards.</summary>
public sealed record PostLikesResponse(int LikeCount, bool LikedByMe);

/// <param name="NextBefore">Send as <c>before</c> to read the next page; null on the last page.</param>
public sealed record PostFeedResponse(IReadOnlyList<PostResponse> Items, DateTimeOffset? NextBefore);

public sealed class CreatePostRequestValidator : AbstractValidator<CreatePostRequest>
{
    public CreatePostRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Text)
                || !string.IsNullOrWhiteSpace(x.PhotoObjectKey)
                || x.MatchId is not null)
            .WithName(nameof(CreatePostRequest.Text))
            .WithMessage("A post needs a text, a photo or a match to share.");

        RuleFor(x => x.Text)
            .Must(text => text is null || text.Trim().Length <= Post.TextMaxLength)
            .WithMessage($"Text must have at most {Post.TextMaxLength} characters.");

        RuleFor(x => x.PhotoObjectKey)
            .MaximumLength(Post.PhotoObjectKeyMaxLength);
    }
}

public enum CreatePostFailure
{
    /// <summary>Over <see cref="PostsHandler.DailyLimit"/> posts in 24 hours.</summary>
    TooMany,

    /// <summary>The photo key was not issued to this user.</summary>
    PhotoNotOwned,

    /// <summary>
    /// The match is neither in the author's history nor one they may invite to; or it was sent
    /// alone (no text or photo) by someone who is not its organizer.
    /// </summary>
    MatchNotAllowed,
}

/// <summary>Creates, lists and deletes posts.</summary>
public sealed class PostsHandler
{
    /// <summary>How many posts one user may publish in 24 hours.</summary>
    public const int DailyLimit = 20;

    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 50;

    private readonly ProfileDbContext _context;
    private readonly IProfilePhotoStorage _photoStorage;
    private readonly IMatchInviteReader _matchInvites;
    private readonly TimeProvider _timeProvider;

    public PostsHandler(
        ProfileDbContext context,
        IProfilePhotoStorage photoStorage,
        IMatchInviteReader matchInvites,
        TimeProvider timeProvider)
    {
        _context = context;
        _photoStorage = photoStorage;
        _matchInvites = matchInvites;
        _timeProvider = timeProvider;
    }

    public async Task<(PostResponse? Post, CreatePostFailure? Failure)> CreateAsync(
        Guid callerId,
        CreatePostRequest request,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        var since = now.AddHours(-24);
        var recent = await _context.Posts
            .CountAsync(p => p.AuthorId == callerId && p.CreatedAt >= since, cancellationToken);
        if (recent >= DailyLimit)
        {
            return (null, CreatePostFailure.TooMany);
        }

        // ponytail: post photos reuse the profile-photo upload URL, so their keys live under
        // profiles/{userId}/. Give posts their own prefix when a second kind of media arrives.
        if (!string.IsNullOrWhiteSpace(request.PhotoObjectKey)
            && !request.PhotoObjectKey.StartsWith($"profiles/{callerId}/", StringComparison.Ordinal))
        {
            return (null, CreatePostFailure.PhotoNotOwned);
        }

        var sharesMatch = string.IsNullOrWhiteSpace(request.Text) && string.IsNullOrWhiteSpace(request.PhotoObjectKey);
        Post post;
        if (sharesMatch)
        {
            // The validator guarantees a match id here.
            var matchId = request.MatchId!.Value;
            var invite = await _matchInvites.FindInvitableAsync(matchId, callerId, cancellationToken);
            if (invite is null || invite.OrganizerId != callerId)
            {
                return (null, CreatePostFailure.MatchNotAllowed);
            }

            // Sharing twice leaves the one card there already is.
            var shared = await _context.Posts.AsNoTracking().FirstOrDefaultAsync(
                p => p.AuthorId == callerId && p.Kind == PostKind.MatchCreated && p.MatchId == matchId,
                cancellationToken);
            if (shared is not null)
            {
                var author = await PostAuthors.LoadAsync(_context, _photoStorage, [callerId], cancellationToken);
                return (await MapAsync(shared, author[callerId], callerId, 0, false, 0, cancellationToken), null);
            }

            post = Post.CreateMatchShare(callerId, matchId, invite.Name, invite.DateTime, now);
        }
        else
        {
            string? matchName = null;
            DateTimeOffset? matchStartsAt = null;
            if (request.MatchId is { } matchId)
            {
                var played = await _context.PlayerMatchHistory
                    .Where(h => h.UserId == callerId && h.MatchId == matchId)
                    .Select(h => new { h.MatchName, h.MatchDateTime })
                    .FirstOrDefaultAsync(cancellationToken);
                if (played is not null)
                {
                    (matchName, matchStartsAt) = (played.MatchName, played.MatchDateTime);
                }
                else if (await _matchInvites.FindInvitableAsync(matchId, callerId, cancellationToken) is { } invite)
                {
                    (matchName, matchStartsAt) = (invite.Name, invite.DateTime);
                }
                else
                {
                    return (null, CreatePostFailure.MatchNotAllowed);
                }
            }

            post = Post.Create(
                callerId, request.Text, request.PhotoObjectKey, request.MatchId, matchName, matchStartsAt, now);
        }

        _context.Posts.Add(post);
        await _context.SaveChangesAsync(cancellationToken);

        var authors = await PostAuthors.LoadAsync(_context, _photoStorage, [callerId], cancellationToken);
        return (await MapAsync(post, authors[callerId], callerId, 0, false, 0, cancellationToken), null);
    }

    /// <summary>
    /// Posts newest first, older than <paramref name="before"/> when given: everyone's, or with
    /// <paramref name="followingOnly"/> those of the players the caller follows plus their own.
    /// </summary>
    public async Task<PostFeedResponse> ListAsync(
        Guid callerId,
        DateTimeOffset? before,
        int limit,
        bool followingOnly,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, MaxPageSize);
        var query = _context.Posts.AsNoTracking();
        if (followingOnly)
        {
            query = query.Where(p => p.AuthorId == callerId
                || _context.Follows.Any(f => f.FollowerId == callerId && f.FollowedId == p.AuthorId));
        }

        if (before is { } cursor)
        {
            // Npgsql only accepts UTC offsets for timestamptz parameters.
            var cursorUtc = cursor.ToUniversalTime();
            query = query.Where(p => p.CreatedAt < cursorUtc);
        }

        var posts = await query
            .OrderByDescending(p => p.CreatedAt)
            .Take(take + 1)
            .ToListAsync(cancellationToken);
        var hasMore = posts.Count > take;
        if (hasMore)
        {
            posts.RemoveAt(take);
        }

        var authors = await PostAuthors.LoadAsync(
            _context, _photoStorage, posts.Select(p => p.AuthorId), cancellationToken);

        var postIds = posts.Select(p => p.Id).ToArray();
        var likeCounts = await _context.PostLikes.AsNoTracking()
            .Where(l => postIds.Contains(l.PostId))
            .GroupBy(l => l.PostId)
            .Select(g => new { PostId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PostId, g => g.Count, cancellationToken);
        var likedByMe = (await _context.PostLikes.AsNoTracking()
            .Where(l => l.UserId == callerId && postIds.Contains(l.PostId))
            .Select(l => l.PostId)
            .ToListAsync(cancellationToken)).ToHashSet();
        var commentCounts = await _context.PostComments.AsNoTracking()
            .Where(c => postIds.Contains(c.PostId))
            .GroupBy(c => c.PostId)
            .Select(g => new { PostId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.PostId, g => g.Count, cancellationToken);

        var items = new List<PostResponse>(posts.Count);
        foreach (var post in posts)
        {
            items.Add(await MapAsync(
                post,
                authors[post.AuthorId],
                callerId,
                likeCounts.GetValueOrDefault(post.Id),
                likedByMe.Contains(post.Id),
                commentCounts.GetValueOrDefault(post.Id),
                cancellationToken));
        }

        return new PostFeedResponse(items, hasMore ? posts[^1].CreatedAt : null);
    }

    /// <returns>null when there is no such post, false when it is someone else's.</returns>
    public async Task<bool?> DeleteAsync(Guid callerId, Guid postId, CancellationToken cancellationToken)
    {
        var post = await _context.Posts.FirstOrDefaultAsync(p => p.Id == postId, cancellationToken);
        if (post is null)
        {
            return null;
        }

        if (post.AuthorId != callerId)
        {
            return false;
        }

        // ponytail: the photo object stays in the bucket. Delete it too when storage cost matters.
        _context.Posts.Remove(post);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>Likes or unlikes a post for the caller; repeating either changes nothing.</summary>
    /// <returns>The post's likes afterwards, or null when there is no such post.</returns>
    public async Task<PostLikesResponse?> SetLikeAsync(
        Guid callerId,
        Guid postId,
        bool liked,
        CancellationToken cancellationToken)
    {
        if (!await _context.Posts.AnyAsync(p => p.Id == postId, cancellationToken))
        {
            return null;
        }

        if (liked)
        {
            // ON CONFLICT keeps a double tap (two requests at once) from failing on the key.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO post_likes (post_id, user_id) VALUES ({postId}, {callerId}) ON CONFLICT DO NOTHING",
                cancellationToken);
        }
        else
        {
            await _context.PostLikes
                .Where(l => l.PostId == postId && l.UserId == callerId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        var count = await _context.PostLikes.CountAsync(l => l.PostId == postId, cancellationToken);
        return new PostLikesResponse(count, liked);
    }

    private async Task<PostResponse> MapAsync(
        Post post,
        PostAuthorResponse author,
        Guid callerId,
        int likeCount,
        bool likedByMe,
        int commentCount,
        CancellationToken cancellationToken)
    {
        var photoUrl = post.PhotoObjectKey is null
            ? null
            : await _photoStorage.GetReadUrlAsync(post.PhotoObjectKey, cancellationToken);

        return new PostResponse(
            post.Id,
            author,
            post.Text,
            photoUrl,
            post.MatchId is { } matchId ? new PostMatchResponse(matchId, post.MatchName ?? string.Empty, post.MatchStartsAt) : null,
            post.CreatedAt,
            Mine: post.AuthorId == callerId,
            likeCount,
            likedByMe,
            commentCount,
            post.Kind.ToString());
    }
}

/// <summary>Who wrote a post or a comment, as the feed shows them.</summary>
internal static class PostAuthors
{
    /// <summary>
    /// The public identity of each of <paramref name="userIds"/>; a user without a profile row
    /// gets the placeholder name.
    /// </summary>
    public static async Task<IReadOnlyDictionary<Guid, PostAuthorResponse>> LoadAsync(
        ProfileDbContext context,
        IProfilePhotoStorage photoStorage,
        IEnumerable<Guid> userIds,
        CancellationToken cancellationToken)
    {
        var ids = userIds.Distinct().ToArray();
        var profiles = await context.PlayerProfiles.AsNoTracking()
            .Where(p => ids.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        var authors = new Dictionary<Guid, PostAuthorResponse>(ids.Length);
        foreach (var id in ids)
        {
            var profile = profiles.GetValueOrDefault(id);
            var photoUrl = string.IsNullOrEmpty(profile?.PhotoObjectKey)
                ? null
                : await photoStorage.GetReadUrlAsync(profile.PhotoObjectKey, cancellationToken);
            authors[id] = new PostAuthorResponse(
                id,
                profile?.DisplayName ?? PlayerProfile.PlaceholderDisplayName,
                profile?.Handle,
                photoUrl);
        }

        return authors;
    }
}

[ApiController]
[Route("api/v1/posts")]
[Authorize]
public sealed class PostsController : ControllerBase
{
    private readonly PostsHandler _handler;
    private readonly IValidator<CreatePostRequest> _validator;

    public PostsController(PostsHandler handler, IValidator<CreatePostRequest> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>
    /// GET /api/v1/posts — the feed, newest first. <c>scope=following</c> keeps only the posts
    /// of the players the caller follows (and their own); anything else is everyone's posts.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PostFeedResponse>> List(
        [FromQuery] DateTimeOffset? before,
        [FromQuery] string? scope,
        [FromQuery] int limit = PostsHandler.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var followingOnly = string.Equals(scope, "following", StringComparison.OrdinalIgnoreCase);
        return Ok(await _handler.ListAsync(callerId, before, limit, followingOnly, cancellationToken));
    }

    /// <summary>POST /api/v1/posts — publish a post as the signed-in user.</summary>
    [HttpPost]
    public async Task<ActionResult<PostResponse>> Create(
        [FromBody] CreatePostRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var validation = await _validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            foreach (var error in validation.Errors)
            {
                ModelState.AddModelError(error.PropertyName, error.ErrorMessage);
            }

            return ValidationProblem(ModelState);
        }

        var (post, failure) = await _handler.CreateAsync(callerId, request, cancellationToken);
        return failure switch
        {
            null => StatusCode(StatusCodes.Status201Created, post),
            CreatePostFailure.TooMany => Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                detail: "Too many posts today. Try again tomorrow."),
            CreatePostFailure.PhotoNotOwned => Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: "The photo was not uploaded by this user."),
            _ => Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: "A post can only be tied to a match the author played, or to an open match still to come that they organize or confirmed. Only the organizer shares a match with no text."),
        };
    }

    /// <summary>DELETE /api/v1/posts/{id} — the author removes their post.</summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return await _handler.DeleteAsync(callerId, id, cancellationToken) switch
        {
            null => NotFound(),
            false => Forbid(),
            true => NoContent(),
        };
    }

    /// <summary>PUT /api/v1/posts/{id}/like — the signed-in user likes the post.</summary>
    [HttpPut("{id:guid}/like")]
    public Task<ActionResult<PostLikesResponse>> Like(Guid id, CancellationToken cancellationToken) =>
        SetLikeAsync(id, liked: true, cancellationToken);

    /// <summary>DELETE /api/v1/posts/{id}/like — the signed-in user takes their like back.</summary>
    [HttpDelete("{id:guid}/like")]
    public Task<ActionResult<PostLikesResponse>> Unlike(Guid id, CancellationToken cancellationToken) =>
        SetLikeAsync(id, liked: false, cancellationToken);

    private async Task<ActionResult<PostLikesResponse>> SetLikeAsync(
        Guid id,
        bool liked,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var likes = await _handler.SetLikeAsync(callerId, id, liked, cancellationToken);
        return likes is null ? NotFound() : Ok(likes);
    }

    private bool TryGetCallerId(out Guid callerId) =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out callerId);
}
