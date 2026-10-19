using System.Security.Claims;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Persistence;
using Quadra.Modules.Profile.Validation;

namespace Quadra.Modules.Profile.Search;

// Finding a player to follow: by @handle only, never by name, so nobody can be looked up by
// their real name (DECISIONS #75). Prefix match, a few results, no one blocked either way.

/// <summary>One player found by handle.</summary>
public sealed record PlayerSearchResult(Guid UserId, string DisplayName, string Handle, string? PhotoUrl, string? Position);

public sealed record PlayerSearchResponse(IReadOnlyList<PlayerSearchResult> Items);

public sealed partial class PlayerSearchHandler
{
    public const string RateLimitPolicy = "player-search";
    public const int MaxResults = 20;

    private readonly ProfileDbContext _context;
    private readonly IProfilePhotoStorage _photoStorage;

    public PlayerSearchHandler(ProfileDbContext context, IProfilePhotoStorage photoStorage)
    {
        _context = context;
        _photoStorage = photoStorage;
    }

    /// <summary>"@an" and "AN" both mean the handle prefix "an": 2 to 20 of a-z, 0-9 and _.</summary>
    public static string? NormalizeQuery(string? query)
    {
        var normalized = ProfileHandle.Normalize(query);
        return QueryPattern().IsMatch(normalized) ? normalized : null;
    }

    public async Task<PlayerSearchResponse> SearchAsync(Guid callerId, string prefix, CancellationToken cancellationToken)
    {
        var profiles = await _context.PlayerProfiles.AsNoTracking()
            .Where(p => p.Handle != null && p.Handle.StartsWith(prefix))
            .Where(p => !_context.Blocks.Any(b =>
                (b.BlockerId == callerId && b.BlockedId == p.UserId) || (b.BlockerId == p.UserId && b.BlockedId == callerId)))
            .OrderBy(p => p.Handle!.Length)
            .ThenBy(p => p.Handle)
            .Take(MaxResults)
            .ToListAsync(cancellationToken);

        var items = new List<PlayerSearchResult>(profiles.Count);
        foreach (var p in profiles)
        {
            var photoUrl = string.IsNullOrEmpty(p.PhotoObjectKey)
                ? null
                : await _photoStorage.GetReadUrlAsync(p.PhotoObjectKey, cancellationToken);
            items.Add(new PlayerSearchResult(p.UserId, p.DisplayName, p.Handle!, photoUrl, p.Position?.ToString()));
        }

        return new PlayerSearchResponse(items);
    }

    [GeneratedRegex("^[a-z0-9_]{2,20}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex QueryPattern();
}

[ApiController]
[Route("api/v1/profiles/search")]
[Authorize]
[EnableRateLimiting(PlayerSearchHandler.RateLimitPolicy)]
public sealed class PlayerSearchController : ControllerBase
{
    private readonly PlayerSearchHandler _handler;

    public PlayerSearchController(PlayerSearchHandler handler)
    {
        _handler = handler;
    }

    /// <summary>GET /api/v1/profiles/search?handle=… — players whose @handle starts with it.</summary>
    [HttpGet]
    public async Task<ActionResult<PlayerSearchResponse>> Search([FromQuery] string? handle, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var callerId))
        {
            return Unauthorized();
        }

        var prefix = PlayerSearchHandler.NormalizeQuery(handle);
        if (prefix is null)
        {
            ModelState.AddModelError(nameof(handle), "handle must be 2 to 20 characters: a-z, 0-9 or _ (an @ in front is fine).");
            return ValidationProblem(ModelState);
        }

        return Ok(await _handler.SearchAsync(callerId, prefix, cancellationToken));
    }
}
