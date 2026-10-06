using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Storage;
using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Applies the caller-editable profile fields (display name, positions, photo reference).
/// Stats and level are never touched here.
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

        var primary = ParsePosition(request.PrimaryPosition);
        var secondary = ParsePosition(request.SecondaryPosition);
        var now = _timeProvider.GetUtcNow();

        found.Profile.UpdateDetails(
            request.DisplayName.Trim(),
            primary,
            secondary,
            request.PhotoObjectKey,
            now);

        await _profiles.UpdateAsync(found.Profile, cancellationToken);

        _logger.LogInformation("Profile updated for {UserId}.", callerId);

        var photoUrl = await GetProfileHandler.ResolvePhotoUrlAsync(
            found.Profile.PhotoObjectKey, _photoStorage, cancellationToken);

        return GetProfileHandler.MapToResponse(found.Profile, found.Stats, photoUrl);
    }

    private static PlayerPosition? ParsePosition(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (Enum.TryParse<PlayerPosition>(value, ignoreCase: false, out var parsed)
            && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new InvalidPositionException(value);
    }
}
