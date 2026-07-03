using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quadra.Modules.Profile.Abstractions;
using Quadra.Modules.Profile.Entities;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Bootstraps an empty <c>player_profiles</c> + <c>player_stats</c> pair for a newly registered
/// user. Idempotent: a no-op when the profile already exists (covers <c>UserRegistered</c> redelivery).
/// </summary>
public sealed class ProvisionProfileHandler : IPlayerProfileProvisioner
{
    private readonly IPlayerProfileRepository _profiles;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ProvisionProfileHandler> _logger;

    public ProvisionProfileHandler(
        IPlayerProfileRepository profiles,
        TimeProvider timeProvider,
        ILogger<ProvisionProfileHandler> logger)
    {
        _profiles = profiles;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task EnsureProfileAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (await _profiles.ExistsAsync(userId, cancellationToken))
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        var profile = PlayerProfile.Provision(userId, now);
        var stats = PlayerStats.Empty(userId, now);

        try
        {
            await _profiles.AddAsync(profile, stats, cancellationToken);
            _logger.LogInformation("Provisioned profile for {UserId}.", userId);
        }
        catch (DbUpdateException)
        {
            // Concurrent provisioning (at-least-once redelivery). The unique user_id constraint
            // guarantees a single profile; treat the conflict as already provisioned.
            _logger.LogInformation("Profile for {UserId} already existed; provisioning skipped.", userId);
        }
    }
}
