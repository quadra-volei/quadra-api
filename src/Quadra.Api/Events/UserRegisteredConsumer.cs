using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Profile.Abstractions;
using Quadra.Shared.Events.Auth;

namespace Quadra.Api.Events;

/// <summary>
/// Consumes the <see cref="UserRegistered"/> event and bootstraps an empty profile for the new user
/// by invoking the Profile-owned <see cref="IPlayerProfileProvisioner"/>. Idempotent (at-least-once
/// delivery is safe because provisioning is a no-op when the profile already exists).
/// </summary>
public sealed class UserRegisteredConsumer : IEventHandler<UserRegistered>
{
    private readonly IPlayerProfileProvisioner _provisioner;
    private readonly ILogger<UserRegisteredConsumer> _logger;

    public UserRegisteredConsumer(
        IPlayerProfileProvisioner provisioner,
        ILogger<UserRegisteredConsumer> logger)
    {
        _provisioner = provisioner;
        _logger = logger;
    }

    public async Task HandleAsync(UserRegistered @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        await _provisioner.EnsureProfileAsync(@event.UserId, cancellationToken);

        _logger.LogInformation("Ensured profile exists for {UserId}.", @event.UserId);
    }
}
