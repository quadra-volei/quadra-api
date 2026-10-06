using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Loads a player's profile, stats and (short-lived) photo read URL, keyed on user id.
/// Serves both <c>GET /profiles/me</c> and <c>GET /profiles/{userId}</c>.
/// </summary>
public sealed class GetProfileHandler
{
    private readonly IPlayerProfileRepository _profiles;
    private readonly IProfilePhotoStorage _photoStorage;

    public GetProfileHandler(IPlayerProfileRepository profiles, IProfilePhotoStorage photoStorage)
    {
        _profiles = profiles;
        _photoStorage = photoStorage;
    }

    public async Task<PlayerProfileResponse> HandleAsync(Guid userId, CancellationToken cancellationToken)
    {
        var found = await _profiles.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new ProfileNotFoundException(userId);

        var photoUrl = await ResolvePhotoUrlAsync(found.Profile.PhotoObjectKey, _photoStorage, cancellationToken);
        return MapToResponse(found.Profile, found.Stats, photoUrl);
    }

    /// <summary>Resolves a short-lived read URL for the photo, or <c>null</c> when no photo is set.</summary>
    internal static async Task<string?> ResolvePhotoUrlAsync(
        string? photoObjectKey,
        IProfilePhotoStorage photoStorage,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrEmpty(photoObjectKey)
            ? null
            : await photoStorage.GetReadUrlAsync(photoObjectKey, cancellationToken);
    }

    /// <summary>Manual mapping from entities to the response DTO. Reused by the update handler.</summary>
    public static PlayerProfileResponse MapToResponse(PlayerProfile profile, PlayerStats stats, string? photoUrl)
    {
        return new PlayerProfileResponse(
            profile.UserId,
            profile.DisplayName,
            profile.PrimaryPosition?.ToString(),
            profile.SecondaryPosition?.ToString(),
            photoUrl,
            profile.Level.ToString(),
            new PlayerStatsResponse(
                stats.MatchesPlayed,
                stats.Wins,
                stats.Losses,
                stats.Draws,
                stats.MvpsReceived),
            profile.CreatedAt,
            profile.UpdatedAt);
    }
}
