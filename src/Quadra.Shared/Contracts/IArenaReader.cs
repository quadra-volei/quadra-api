namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module query for the curated arenas. Declared in <c>Quadra.Shared</c>;
/// implemented in <c>Quadra.Modules.Geo</c>. Consumed by Matches to link a game to an arena.
/// Only active arenas count; an arena merged into another answers with the one that stayed.
/// </summary>
public interface IArenaReader
{
    /// <summary>The active arena <paramref name="arenaId"/> is (or was merged into); null when there is none.</summary>
    Task<Guid?> FindActiveAsync(Guid arenaId, CancellationToken cancellationToken);

    /// <summary>The active arena a Google place id is linked to; null when the place is linked to none.</summary>
    Task<Guid?> FindActiveByPlaceAsync(string placeId, CancellationToken cancellationToken);
}
