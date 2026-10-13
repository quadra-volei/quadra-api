using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.Follows;

// The connection between two accounts: one player follows another, with no approval step.
// It is what the feed's "Amigos" tab filters by.

/// <summary>One player following another; at most one row per pair.</summary>
public sealed class Follow
{
    public Guid FollowerId { get; init; }

    public Guid FollowedId { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
}

public sealed class FollowConfiguration : IEntityTypeConfiguration<Follow>
{
    public void Configure(EntityTypeBuilder<Follow> builder)
    {
        builder.ToTable("follows");

        builder.HasKey(f => new { f.FollowerId, f.FollowedId });
        builder.Property(f => f.FollowerId).HasColumnName("follower_id");
        builder.Property(f => f.FollowedId).HasColumnName("followed_id");
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()").IsRequired();

        // The key serves "who do I follow"; this one serves "who follows them".
        builder.HasIndex(f => f.FollowedId).HasDatabaseName("ix_follows_followed_id");
    }
}

/// <summary>A player's connections, as seen by the caller.</summary>
/// <param name="FollowersCount">How many players follow them.</param>
/// <param name="FollowingCount">How many players they follow.</param>
/// <param name="FollowedByMe">Whether the caller follows them.</param>
public sealed record FollowStateResponse(int FollowersCount, int FollowingCount, bool FollowedByMe);

/// <summary>Reads and changes who follows whom.</summary>
public sealed class FollowsHandler
{
    private readonly ProfileDbContext _context;

    public FollowsHandler(ProfileDbContext context)
    {
        _context = context;
    }

    /// <returns>The connections of <paramref name="userId"/>, or null when there is no such player.</returns>
    public async Task<FollowStateResponse?> GetAsync(Guid callerId, Guid userId, CancellationToken cancellationToken)
    {
        if (!await _context.PlayerProfiles.AnyAsync(p => p.UserId == userId, cancellationToken))
        {
            return null;
        }

        return new FollowStateResponse(
            await _context.Follows.CountAsync(f => f.FollowedId == userId, cancellationToken),
            await _context.Follows.CountAsync(f => f.FollowerId == userId, cancellationToken),
            await _context.Follows.AnyAsync(f => f.FollowerId == callerId && f.FollowedId == userId, cancellationToken));
    }

    /// <summary>
    /// Makes the caller follow or stop following <paramref name="userId"/>; repeating either
    /// changes nothing. The caller is never <paramref name="userId"/> (the controller refuses it).
    /// </summary>
    /// <returns>The connections afterwards, or null when there is no such player.</returns>
    public async Task<FollowStateResponse?> SetAsync(
        Guid callerId,
        Guid userId,
        bool following,
        CancellationToken cancellationToken)
    {
        if (!await _context.PlayerProfiles.AnyAsync(p => p.UserId == userId, cancellationToken))
        {
            return null;
        }

        if (following)
        {
            // ON CONFLICT keeps a double tap (two requests at once) from failing on the key.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT INTO follows (follower_id, followed_id) VALUES ({callerId}, {userId}) ON CONFLICT DO NOTHING",
                cancellationToken);
        }
        else
        {
            await _context.Follows
                .Where(f => f.FollowerId == callerId && f.FollowedId == userId)
                .ExecuteDeleteAsync(cancellationToken);
        }

        return await GetAsync(callerId, userId, cancellationToken);
    }
}

[ApiController]
[Route("api/v1/profiles/{userId:guid}/follow")]
[Authorize]
public sealed class FollowsController : ControllerBase
{
    private readonly FollowsHandler _handler;

    public FollowsController(FollowsHandler handler)
    {
        _handler = handler;
    }

    /// <summary>GET /api/v1/profiles/{userId}/follow — the player's connections.</summary>
    [HttpGet]
    public async Task<ActionResult<FollowStateResponse>> Get(Guid userId, CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        var state = await _handler.GetAsync(callerId, userId, cancellationToken);
        return state is null ? NotFound() : Ok(state);
    }

    /// <summary>PUT /api/v1/profiles/{userId}/follow — the signed-in user follows the player.</summary>
    [HttpPut]
    public Task<ActionResult<FollowStateResponse>> Follow(Guid userId, CancellationToken cancellationToken) =>
        SetAsync(userId, following: true, cancellationToken);

    /// <summary>DELETE /api/v1/profiles/{userId}/follow — the signed-in user stops following.</summary>
    [HttpDelete]
    public Task<ActionResult<FollowStateResponse>> Unfollow(Guid userId, CancellationToken cancellationToken) =>
        SetAsync(userId, following: false, cancellationToken);

    private async Task<ActionResult<FollowStateResponse>> SetAsync(
        Guid userId,
        bool following,
        CancellationToken cancellationToken)
    {
        if (!TryGetCallerId(out var callerId))
        {
            return Unauthorized();
        }

        if (callerId == userId)
        {
            return Problem(
                statusCode: StatusCodes.Status422UnprocessableEntity,
                detail: "A player cannot follow themselves.");
        }

        var state = await _handler.SetAsync(callerId, userId, following, cancellationToken);
        return state is null ? NotFound() : Ok(state);
    }

    private bool TryGetCallerId(out Guid callerId) =>
        Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out callerId);
}
