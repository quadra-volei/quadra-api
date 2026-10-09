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

namespace Quadra.Modules.Profile.Posts;

// The whole first version of the "Rede" feed: a player publishes a text and/or one photo,
// optionally tied to a match they played, and everyone reads one feed, newest first.
// No follows, likes or comments yet.

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

    public string? Text { get; private set; }

    public string? PhotoObjectKey { get; private set; }

    /// <summary>App-layer FK to <c>matches.id</c>. No DDL FK (module boundary rule).</summary>
    public Guid? MatchId { get; private set; }

    /// <summary>The match's name when the post was written (snapshot from the author's history).</summary>
    public string? MatchName { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Post Create(
        Guid authorId,
        string? text,
        string? photoObjectKey,
        Guid? matchId,
        string? matchName,
        DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            AuthorId = authorId,
            Text = string.IsNullOrWhiteSpace(text) ? null : text.Trim(),
            PhotoObjectKey = string.IsNullOrWhiteSpace(photoObjectKey) ? null : photoObjectKey,
            MatchId = matchId,
            MatchName = matchName,
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
        builder.Property(p => p.Text).HasColumnName("text").HasMaxLength(Post.TextMaxLength);
        builder.Property(p => p.PhotoObjectKey).HasColumnName("photo_object_key").HasMaxLength(Post.PhotoObjectKeyMaxLength);
        builder.Property(p => p.MatchId).HasColumnName("match_id");
        builder.Property(p => p.MatchName).HasColumnName("match_name").HasMaxLength(Post.MatchNameMaxLength);
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        // The feed reads newest first; the daily limit counts one author's recent posts; the
        // match index is for listing a match's posts.
        builder.HasIndex(p => p.CreatedAt).HasDatabaseName("ix_posts_created_at");
        builder.HasIndex(p => new { p.AuthorId, p.CreatedAt }).HasDatabaseName("ix_posts_author_id_created_at");
        builder.HasIndex(p => p.MatchId).HasDatabaseName("ix_posts_match_id");
    }
}

/// <summary>Body of <c>POST /api/v1/posts</c>. At least one of text and photo is required.</summary>
/// <param name="Text">Up to 500 characters after trimming.</param>
/// <param name="PhotoObjectKey">
/// The <c>objectKey</c> returned by <c>POST /api/v1/profiles/me/photo/upload-url</c>, after the
/// app uploaded the photo to it.
/// </param>
/// <param name="MatchId">A finished match from the author's own history.</param>
public sealed record CreatePostRequest(string? Text, string? PhotoObjectKey = null, Guid? MatchId = null);

public sealed record PostAuthorResponse(Guid UserId, string DisplayName, string? Handle, string? PhotoUrl);

public sealed record PostMatchResponse(Guid Id, string Name);

public sealed record PostResponse(
    Guid Id,
    PostAuthorResponse Author,
    string? Text,
    string? PhotoUrl,
    PostMatchResponse? Match,
    DateTimeOffset CreatedAt,
    bool Mine);

/// <param name="NextBefore">Send as <c>before</c> to read the next page; null on the last page.</param>
public sealed record PostFeedResponse(IReadOnlyList<PostResponse> Items, DateTimeOffset? NextBefore);

public sealed class CreatePostRequestValidator : AbstractValidator<CreatePostRequest>
{
    public CreatePostRequestValidator()
    {
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Text) || !string.IsNullOrWhiteSpace(x.PhotoObjectKey))
            .WithName(nameof(CreatePostRequest.Text))
            .WithMessage("A post needs a text or a photo.");

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

    /// <summary>The match is not in the author's history.</summary>
    MatchNotPlayed,
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
    private readonly TimeProvider _timeProvider;

    public PostsHandler(ProfileDbContext context, IProfilePhotoStorage photoStorage, TimeProvider timeProvider)
    {
        _context = context;
        _photoStorage = photoStorage;
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

        string? matchName = null;
        if (request.MatchId is { } matchId)
        {
            matchName = await _context.PlayerMatchHistory
                .Where(h => h.UserId == callerId && h.MatchId == matchId)
                .Select(h => h.MatchName)
                .FirstOrDefaultAsync(cancellationToken);
            if (matchName is null)
            {
                return (null, CreatePostFailure.MatchNotPlayed);
            }
        }

        var post = Post.Create(callerId, request.Text, request.PhotoObjectKey, request.MatchId, matchName, now);
        _context.Posts.Add(post);
        await _context.SaveChangesAsync(cancellationToken);

        var author = await _context.PlayerProfiles.AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == callerId, cancellationToken);
        return (await MapAsync(post, author, callerId, cancellationToken), null);
    }

    /// <summary>Everyone's posts, newest first, older than <paramref name="before"/> when given.</summary>
    public async Task<PostFeedResponse> ListAsync(
        Guid callerId,
        DateTimeOffset? before,
        int limit,
        CancellationToken cancellationToken)
    {
        var take = Math.Clamp(limit, 1, MaxPageSize);
        var query = _context.Posts.AsNoTracking();
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

        var authorIds = posts.Select(p => p.AuthorId).Distinct().ToArray();
        var authors = await _context.PlayerProfiles.AsNoTracking()
            .Where(p => authorIds.Contains(p.UserId))
            .ToDictionaryAsync(p => p.UserId, cancellationToken);

        var items = new List<PostResponse>(posts.Count);
        foreach (var post in posts)
        {
            items.Add(await MapAsync(post, authors.GetValueOrDefault(post.AuthorId), callerId, cancellationToken));
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

    private async Task<PostResponse> MapAsync(
        Post post,
        PlayerProfile? author,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        var authorPhotoUrl = string.IsNullOrEmpty(author?.PhotoObjectKey)
            ? null
            : await _photoStorage.GetReadUrlAsync(author.PhotoObjectKey, cancellationToken);
        var photoUrl = post.PhotoObjectKey is null
            ? null
            : await _photoStorage.GetReadUrlAsync(post.PhotoObjectKey, cancellationToken);

        return new PostResponse(
            post.Id,
            new PostAuthorResponse(
                post.AuthorId,
                author?.DisplayName ?? PlayerProfile.PlaceholderDisplayName,
                author?.Handle,
                authorPhotoUrl),
            post.Text,
            photoUrl,
            post.MatchId is { } matchId ? new PostMatchResponse(matchId, post.MatchName ?? string.Empty) : null,
            post.CreatedAt,
            Mine: post.AuthorId == callerId);
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

    /// <summary>GET /api/v1/posts — the feed, newest first.</summary>
    [HttpGet]
    public async Task<ActionResult<PostFeedResponse>> List(
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int limit = PostsHandler.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return Ok(await _handler.ListAsync(callerId, before, limit, cancellationToken));
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
                detail: "A post can only be tied to a finished match the author played."),
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

    private bool TryGetCallerId(out Guid callerId) =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out callerId);
}
