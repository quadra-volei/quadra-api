using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Moderation;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.Posts;

// Comments on a post of the "Rede" feed: a flat conversation, oldest first. No replies to a
// comment, no likes on a comment, no editing.

/// <summary>One comment. Rows are inserted and deleted, never edited.</summary>
public sealed class PostComment
{
    public const int TextMaxLength = 300;

    // Private parameterless constructor required by EF Core.
    private PostComment() { }

    public Guid Id { get; private set; }

    public Guid PostId { get; private set; }

    public Guid AuthorId { get; private set; }

    public string Text { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public static PostComment Create(Guid postId, Guid authorId, string text, DateTimeOffset now) =>
        new()
        {
            Id = Guid.NewGuid(),
            PostId = postId,
            AuthorId = authorId,
            Text = text.Trim(),
            CreatedAt = now,
        };
}

public sealed class PostCommentConfiguration : IEntityTypeConfiguration<PostComment>
{
    public void Configure(EntityTypeBuilder<PostComment> builder)
    {
        builder.ToTable("post_comments");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.PostId).HasColumnName("post_id").IsRequired();
        builder.Property(c => c.AuthorId).HasColumnName("author_id").IsRequired();
        builder.Property(c => c.Text).HasColumnName("text").HasMaxLength(PostComment.TextMaxLength).IsRequired();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        // Same module, so a real FK: deleting a post takes its comments with it.
        builder.HasOne<Post>().WithMany().HasForeignKey(c => c.PostId).OnDelete(DeleteBehavior.Cascade);

        // A post's conversation in order; one author's recent comments for the daily limit.
        builder.HasIndex(c => new { c.PostId, c.CreatedAt }).HasDatabaseName("ix_post_comments_post_id_created_at");
        builder.HasIndex(c => new { c.AuthorId, c.CreatedAt }).HasDatabaseName("ix_post_comments_author_id_created_at");
    }
}

/// <summary>Body of <c>POST /api/v1/posts/{postId}/comments</c>.</summary>
/// <param name="Text">1 to 300 characters after trimming.</param>
public sealed record CreateCommentRequest(string? Text);

/// <param name="CanDelete">True for the comment's author and for the post's author.</param>
public sealed record CommentResponse(
    Guid Id,
    PostAuthorResponse Author,
    string Text,
    DateTimeOffset CreatedAt,
    bool CanDelete);

public sealed record CommentListResponse(IReadOnlyList<CommentResponse> Items);

public sealed class CreateCommentRequestValidator : AbstractValidator<CreateCommentRequest>
{
    public CreateCommentRequestValidator()
    {
        RuleFor(x => x.Text)
            .Must(text => !string.IsNullOrWhiteSpace(text))
            .WithMessage("Text is required.")
            .Must(text => text is null || text.Trim().Length <= PostComment.TextMaxLength)
            .WithMessage($"Text must have at most {PostComment.TextMaxLength} characters.");
    }
}

/// <summary>Lists, creates and deletes the comments of a post.</summary>
public sealed class CommentsHandler
{
    /// <summary>How many comments one user may write in 24 hours.</summary>
    public const int DailyLimit = 100;

    // ponytail: a post shows its first 200 comments, with no paging. Page by `after` when a
    // post gets a longer conversation than that.
    public const int MaxListed = 200;

    private readonly ProfileDbContext _context;
    private readonly IProfilePhotoStorage _photoStorage;
    private readonly TimeProvider _timeProvider;

    public CommentsHandler(ProfileDbContext context, IProfilePhotoStorage photoStorage, TimeProvider timeProvider)
    {
        _context = context;
        _photoStorage = photoStorage;
        _timeProvider = timeProvider;
    }

    /// <returns>The post's comments, oldest first, or null when there is no such post.</returns>
    public async Task<CommentListResponse?> ListAsync(Guid callerId, Guid postId, CancellationToken cancellationToken)
    {
        var postAuthorId = await FindVisiblePostAuthorAsync(callerId, postId, cancellationToken);
        if (postAuthorId is null)
        {
            return null;
        }

        var comments = await _context.PostComments.AsNoTracking()
            .Where(c => c.PostId == postId)
            .Where(c => !_context.Blocks.Any(b =>
                (b.BlockerId == callerId && b.BlockedId == c.AuthorId) || (b.BlockerId == c.AuthorId && b.BlockedId == callerId)))
            .OrderBy(c => c.CreatedAt)
            .Take(MaxListed)
            .ToListAsync(cancellationToken);
        var authors = await PostAuthors.LoadAsync(
            _context, _photoStorage, comments.Select(c => c.AuthorId), cancellationToken);

        return new CommentListResponse(comments
            .Select(c => Map(c, authors[c.AuthorId], callerId, postAuthorId.Value))
            .ToList());
    }

    /// <returns>
    /// The stored comment; or (null, false) when there is no such post and (null, true) when the
    /// user is over the daily limit.
    /// </returns>
    public async Task<(CommentResponse? Comment, bool TooMany)> CreateAsync(
        Guid callerId,
        Guid postId,
        CreateCommentRequest request,
        CancellationToken cancellationToken)
    {
        var postAuthorId = await FindVisiblePostAuthorAsync(callerId, postId, cancellationToken);
        if (postAuthorId is null)
        {
            return (null, false);
        }

        var now = _timeProvider.GetUtcNow();
        var since = now.AddHours(-24);
        var recent = await _context.PostComments
            .CountAsync(c => c.AuthorId == callerId && c.CreatedAt >= since, cancellationToken);
        if (recent >= DailyLimit)
        {
            return (null, true);
        }

        var comment = PostComment.Create(postId, callerId, request.Text!, now);
        _context.PostComments.Add(comment);
        await _context.SaveChangesAsync(cancellationToken);

        var authors = await PostAuthors.LoadAsync(_context, _photoStorage, [callerId], cancellationToken);
        return (Map(comment, authors[callerId], callerId, postAuthorId.Value), false);
    }

    /// <returns>null when there is no such comment on that post, false when the caller may not delete it.</returns>
    public async Task<bool?> DeleteAsync(Guid callerId, Guid postId, Guid commentId, CancellationToken cancellationToken)
    {
        var comment = await _context.PostComments
            .FirstOrDefaultAsync(c => c.Id == commentId && c.PostId == postId, cancellationToken);
        if (comment is null)
        {
            return null;
        }

        var postAuthorId = await FindPostAuthorAsync(postId, cancellationToken);
        if (comment.AuthorId != callerId && postAuthorId != callerId)
        {
            return false;
        }

        _context.PostComments.Remove(comment);
        await _context.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>The post's author, or null when there is no such post or a block stands between them and the caller.</summary>
    private async Task<Guid?> FindVisiblePostAuthorAsync(Guid callerId, Guid postId, CancellationToken cancellationToken)
    {
        var authorId = await FindPostAuthorAsync(postId, cancellationToken);
        return authorId is { } id && await _context.Blocks.AnyBetweenAsync(callerId, id, cancellationToken) ? null : authorId;
    }

    private async Task<Guid?> FindPostAuthorAsync(Guid postId, CancellationToken cancellationToken) =>
        await _context.Posts.AsNoTracking()
            .Where(p => p.Id == postId)
            .Select(p => (Guid?)p.AuthorId)
            .FirstOrDefaultAsync(cancellationToken);

    private static CommentResponse Map(PostComment comment, PostAuthorResponse author, Guid callerId, Guid postAuthorId) =>
        new(
            comment.Id,
            author,
            comment.Text,
            comment.CreatedAt,
            CanDelete: comment.AuthorId == callerId || postAuthorId == callerId);
}

[ApiController]
[Route("api/v1/posts/{postId:guid}/comments")]
[Authorize]
public sealed class PostCommentsController : ControllerBase
{
    private readonly CommentsHandler _handler;
    private readonly IValidator<CreateCommentRequest> _validator;

    public PostCommentsController(CommentsHandler handler, IValidator<CreateCommentRequest> validator)
    {
        _handler = handler;
        _validator = validator;
    }

    /// <summary>GET /api/v1/posts/{postId}/comments — the post's conversation, oldest first.</summary>
    [HttpGet]
    public async Task<ActionResult<CommentListResponse>> List(Guid postId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var comments = await _handler.ListAsync(callerId, postId, cancellationToken);
        return comments is null ? NotFound() : Ok(comments);
    }

    /// <summary>POST /api/v1/posts/{postId}/comments — the signed-in user comments on the post.</summary>
    [HttpPost]
    public async Task<ActionResult<CommentResponse>> Create(
        Guid postId,
        [FromBody] CreateCommentRequest request,
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

        var (comment, tooMany) = await _handler.CreateAsync(callerId, postId, request, cancellationToken);
        if (comment is not null)
        {
            return StatusCode(StatusCodes.Status201Created, comment);
        }

        return tooMany
            ? Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                detail: "Too many comments today. Try again tomorrow.")
            : NotFound();
    }

    /// <summary>
    /// DELETE /api/v1/posts/{postId}/comments/{commentId} — by the comment's author or the
    /// post's author.
    /// </summary>
    [HttpDelete("{commentId:guid}")]
    public async Task<IActionResult> Delete(Guid postId, Guid commentId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        return await _handler.DeleteAsync(callerId, postId, commentId, cancellationToken) switch
        {
            null => NotFound(),
            false => Forbid(),
            true => NoContent(),
        };
    }

    private bool TryGetCallerId(out Guid callerId) =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out callerId);
}
