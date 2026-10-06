using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;
using Quadra.Modules.Profile.Validation;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Applies <c>PUT /api/v1/profiles/me</c>. The first accepted update completes onboarding: it
/// must carry the preferred modality and the self-declared level, which is then locked. Later
/// updates edit the profile; the level can no longer be declared.
/// </summary>
public sealed class UpdateProfileHandler
{
    private readonly IPlayerProfileRepository _profiles;
    private readonly IProfilePhotoStorage _photoStorage;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<UpdateProfileHandler> _logger;

    public UpdateProfileHandler(
        IPlayerProfileRepository profiles,
        IProfilePhotoStorage photoStorage,
        TimeProvider timeProvider,
        ILogger<UpdateProfileHandler> logger)
    {
        _profiles = profiles;
        _photoStorage = photoStorage;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PlayerProfileResponse> HandleAsync(
        Guid callerId,
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        var found = await _profiles.FindByUserIdAsync(callerId, cancellationToken)
            ?? throw new ProfileNotFoundException(callerId);
        var profile = found.Profile;

        // The validator has already checked the formats; these parses cannot fail.
        var position = Enum.Parse<PlayerPosition>(request.Position);
        PlayerModality? modality = request.Modality is null ? null : Enum.Parse<PlayerModality>(request.Modality);
        PlayerLevel? declaredLevel = request.Level is null ? null : Enum.Parse<PlayerLevel>(request.Level);

        var completesOnboarding = !profile.IsOnboardingCompleted;
        if (completesOnboarding)
        {
            if (modality is null)
            {
                throw new ProfileUpdateRejectedException(
                    nameof(UpdateProfileRequest.Modality), "Modality is required to complete onboarding.");
            }

            if (declaredLevel is null)
            {
                throw new ProfileUpdateRejectedException(
                    nameof(UpdateProfileRequest.Level), "Level is required to complete onboarding.");
            }
        }
        else if (declaredLevel is not null && declaredLevel != profile.DeclaredLevel)
        {
            throw new ProfileUpdateRejectedException(
                nameof(UpdateProfileRequest.Level), "Level can only be declared during onboarding.");
        }

        var handle = ProfileHandle.Normalize(request.Handle);
        if (await _profiles.IsHandleTakenAsync(handle, callerId, cancellationToken))
        {
            throw new HandleAlreadyTakenException(handle);
        }

        var now = _timeProvider.GetUtcNow();

        profile.UpdateDetails(
            request.FirstName.Trim(),
            request.LastName.Trim(),
            handle,
            request.BirthDate,
            position,
            request.PhotoObjectKey,
            now);

        if (modality is { } preferredModality)
        {
            profile.SetPreferredModality(preferredModality, now);
        }

        if (completesOnboarding)
        {
            profile.CompleteOnboarding(declaredLevel!.Value, now);
            profile.SetLevel(PlayerLevelCalculator.Calculate(found.Stats, profile.DeclaredLevel), now);
        }

        // False when another player claimed the handle between the check above and this save.
        if (!await _profiles.TryUpdateAsync(profile, cancellationToken))
        {
            throw new HandleAlreadyTakenException(handle);
        }

        _logger.LogInformation(
            "Profile updated for {UserId}. {OnboardingCompleted}",
            callerId,
            completesOnboarding);

        var photoUrl = await GetProfileHandler.ResolvePhotoUrlAsync(
            profile.PhotoObjectKey, _photoStorage, cancellationToken);

        return GetProfileHandler.MapToResponse(profile, found.Stats, photoUrl, includePrivate: true);
    }
}
