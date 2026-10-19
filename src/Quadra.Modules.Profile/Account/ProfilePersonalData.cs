using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Profile.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Profile.Account;

/// <summary>
/// Profile part of account export and deletion (SEC-14): profile, stats, match history, card,
/// posts (with the likes and comments on them), comments, likes, follows both ways, activities
/// and feedback. Deletion removes all of it.
/// </summary>
public sealed class ProfilePersonalData : IPersonalDataStore
{
    private readonly ProfileDbContext _db;

    public ProfilePersonalData(ProfileDbContext db)
    {
        _db = db;
    }

    public string Section => "profile";

    public async Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken) => new
    {
        Profile = await _db.PlayerProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken),
        Stats = await _db.PlayerStats.AsNoTracking().FirstOrDefaultAsync(s => s.UserId == userId, cancellationToken),
        Card = await _db.PlayerCards.AsNoTracking().FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken),
        MatchHistory = await _db.PlayerMatchHistory.AsNoTracking().Where(h => h.UserId == userId).ToListAsync(cancellationToken),
        Posts = await _db.Posts.AsNoTracking().Where(p => p.AuthorId == userId).ToListAsync(cancellationToken),
        Comments = await _db.PostComments.AsNoTracking().Where(c => c.AuthorId == userId).ToListAsync(cancellationToken),
        Likes = await _db.PostLikes.AsNoTracking().Where(l => l.UserId == userId).ToListAsync(cancellationToken),
        Following = await _db.Follows.AsNoTracking().Where(f => f.FollowerId == userId)
            .Select(f => new { f.FollowedId, f.CreatedAt }).ToListAsync(cancellationToken),
        Followers = await _db.Follows.AsNoTracking().Where(f => f.FollowedId == userId)
            .Select(f => new { f.FollowerId, f.CreatedAt }).ToListAsync(cancellationToken),
        Activities = await _db.Activities.AsNoTracking().Where(a => a.ActorId == userId).ToListAsync(cancellationToken),
        Feedback = await _db.Feedback.AsNoTracking().Where(f => f.UserId == userId).ToListAsync(cancellationToken),
    };

    // ponytail: photo objects in the bucket are not deleted (R2 is not configured yet); add a
    // delete to IProfilePhotoStorage when it is.
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        await _db.PostComments.Where(c => c.AuthorId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.PostLikes.Where(l => l.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.Posts.Where(p => p.AuthorId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.Follows.Where(f => f.FollowerId == userId || f.FollowedId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.Activities.Where(a => a.ActorId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.Feedback.Where(f => f.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.PlayerMatchHistory.Where(h => h.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.PlayerCards.Where(c => c.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.PlayerStats.Where(s => s.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.PlayerProfiles.Where(p => p.UserId == userId).ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
