using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Profile.Abstractions;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.Posts;

/// <summary>
/// <see cref="IActivityFeedWriter"/> over the <c>posts</c> table: an activity card is a post
/// with no text or photo, a <see cref="PostKind"/> other than <see cref="PostKind.Post"/> and
/// the match it is about. It is liked, commented on and deleted like any other post.
/// </summary>
public sealed class ActivityFeedWriter : IActivityFeedWriter
{
    private readonly ProfileDbContext _context;
    private readonly TimeProvider _timeProvider;

    public ActivityFeedWriter(ProfileDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task AddMatchActivityAsync(
        Guid playerId,
        MatchActivity activity,
        Guid matchId,
        string matchName,
        DateTimeOffset matchStartsAt,
        CancellationToken cancellationToken)
    {
        var kind = activity switch
        {
            MatchActivity.Created => PostKind.MatchCreated,
            MatchActivity.Confirmed => PostKind.PresenceConfirmed,
            _ => throw new ArgumentOutOfRangeException(nameof(activity), activity, "Unknown activity."),
        };

        var exists = await _context.Posts.AnyAsync(
            p => p.AuthorId == playerId && p.Kind == kind && p.MatchId == matchId,
            cancellationToken);
        if (exists)
        {
            return;
        }

        _context.Posts.Add(Post.CreateActivity(
            playerId, kind, matchId, matchName, matchStartsAt, _timeProvider.GetUtcNow()));
        await _context.SaveChangesAsync(cancellationToken);
    }
}
