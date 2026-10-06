using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;
using Quadra.Modules.Profile.Validation;

namespace Quadra.Modules.Profile.Application;

public sealed class GetProfileHandler
{
    private readonly IPlayerProfileRepository _profiles;
    private readonly IProfilePhotoStorage _photoStorage;

    public GetProfileHandler(IPlayerProfileRepository profiles, IProfilePhotoStorage photoStorage)
    {
        _profiles = profiles;
        _photoStorage = photoStorage;
    }

    /// <summary>
    /// Returns the profile of <paramref name="userId"/>. <paramref name="callerId"/> decides
    /// whether owner-only fields (birth date, photo object key) are included.
    /// </summary>
    public async Task<PlayerProfileResponse> HandleAsync(
        Guid userId,
        Guid? callerId,
        CancellationToken cancellationToken)
    {
        var found = await _profiles.FindByUserIdAsync(userId, cancellationToken)
            ?? throw new ProfileNotFoundException(userId);

        var photoUrl = await ResolvePhotoUrlAsync(found.Profile.PhotoObjectKey, _photoStorage, cancellationToken);
        return MapToResponse(found.Profile, found.Stats, photoUrl, includePrivate: callerId == userId);
    }

    /// <summary>
    /// Whether <paramref name="handle"/> can be used by <paramref name="callerId"/>: free, or
    /// already their own. The handle must already be valid.
    /// </summary>
    public async Task<HandleAvailabilityResponse> CheckHandleAsync(
        string handle,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        var normalized = ProfileHandle.Normalize(handle);
        var taken = await _profiles.IsHandleTakenAsync(normalized, callerId, cancellationToken);
        return new HandleAvailabilityResponse(normalized, Available: !taken);
    }

    internal static async Task<string?> ResolvePhotoUrlAsync(
        string? photoObjectKey,
        IProfilePhotoStorage photoStorage,
        CancellationToken cancellationToken)
    {
        return string.IsNullOrEmpty(photoObjectKey)
            ? null
            : await photoStorage.GetReadUrlAsync(photoObjectKey, cancellationToken);
    }

    public static PlayerProfileResponse MapToResponse(
        PlayerProfile profile,
        PlayerStats stats,
        string? photoUrl,
        bool includePrivate)
    {
        var skills = PlayerSkillCalculator.Calculate(profile.DeclaredLevel, profile.Position);

        return new PlayerProfileResponse(
            profile.UserId,
            profile.DisplayName,
            profile.FirstName,
            profile.LastName,
            profile.Handle,
            includePrivate ? profile.BirthDate : null,
            profile.Position?.ToString(),
            profile.PreferredModality?.ToString(),
            profile.DeclaredLevel?.ToString(),
            profile.Level.ToString(),
            photoUrl,
            includePrivate ? profile.PhotoObjectKey : null,
            profile.IsOnboardingCompleted,
            new PlayerSkillsResponse(
                skills.Overall,
                skills.Ace,
                skills.Block,
                skills.Attack,
                skills.Defense),
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
