using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Which arena a created or edited game is at (DECISIONS #92). The organizer's choice
/// (<c>arenaId</c>, from the app's "É no …?") must be an active arena; otherwise the picked Google
/// place links the game when the place belongs to an arena. Distance alone never links a game.
/// </summary>
public sealed class MatchArenaResolver
{
    private readonly IArenaReader _arenas;

    public MatchArenaResolver(IArenaReader arenas)
    {
        _arenas = arenas;
    }

    /// <summary>
    /// The arena to link, or null for none. Throws <see cref="MatchArenaUnavailableException"/>
    /// when <paramref name="arenaId"/> is not an active arena.
    /// </summary>
    public async Task<Guid?> ResolveAsync(Guid? arenaId, string? placeId, CancellationToken cancellationToken)
    {
        if (arenaId is { } chosen)
        {
            return await _arenas.FindActiveAsync(chosen, cancellationToken)
                ?? throw new MatchArenaUnavailableException(chosen);
        }

        return string.IsNullOrWhiteSpace(placeId)
            ? null
            : await _arenas.FindActiveByPlaceAsync(placeId, cancellationToken);
    }
}
