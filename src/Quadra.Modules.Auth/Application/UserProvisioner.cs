using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Auth.Entities;
using Quadra.Modules.Auth.Persistence;
using Quadra.Shared.Events.Auth;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// The user a login resolved to, and whether this login created it.
/// </summary>
public sealed record ProvisionedUser(User User, bool IsNew);

/// <summary>
/// Finds the local user for a proven identity, creating it on first login — there is no separate
/// signup step. Publishes <see cref="UserRegistered"/> when a row is created. Callers must only
/// invoke it after the identity is proven (approved OTP or validated ID token).
/// </summary>
public sealed class UserProvisioner
{
    private readonly IUserRepository _userRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<UserProvisioner> _logger;

    public UserProvisioner(
        IUserRepository userRepository,
        IEventPublisher eventPublisher,
        ILogger<UserProvisioner> logger)
    {
        _userRepository = userRepository;
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<ProvisionedUser> GetOrCreateForPhoneAsync(
        string phoneNumber,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        var existing = await _userRepository.FindByPhoneNumberAsync(phoneNumber, cancellationToken);
        if (existing is not null)
        {
            return new ProvisionedUser(existing, IsNew: false);
        }

        var user = User.CreateForPhone(Guid.NewGuid(), phoneNumber, now);
        if (await _userRepository.TryAddAsync(user, cancellationToken))
        {
            await PublishUserRegisteredAsync(user, now, cancellationToken);
            return new ProvisionedUser(user, IsNew: true);
        }

        // A concurrent login for the same phone created the row first.
        var winner = await _userRepository.FindByPhoneNumberAsync(phoneNumber, cancellationToken)
            ?? throw new InvalidOperationException("User insert conflicted but no existing row was found.");
        return new ProvisionedUser(winner, IsNew: false);
    }

    public async Task<ProvisionedUser> GetOrCreateForExternalAsync(
        IdentityProvider provider,
        string externalSubject,
        string? email,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(externalSubject);

        var existing = await _userRepository
            .FindByExternalIdentityAsync(provider, externalSubject, cancellationToken);
        if (existing is not null)
        {
            return new ProvisionedUser(existing, IsNew: false);
        }

        var normalizedEmail = string.IsNullOrWhiteSpace(email)
            ? null
            : email.Trim().ToLowerInvariant();

        var user = User.CreateForExternal(Guid.NewGuid(), provider, externalSubject, normalizedEmail, now);
        if (await _userRepository.TryAddAsync(user, cancellationToken))
        {
            await PublishUserRegisteredAsync(user, now, cancellationToken);
            return new ProvisionedUser(user, IsNew: true);
        }

        // A concurrent login for the same external identity created the row first.
        var winner = await _userRepository
            .FindByExternalIdentityAsync(provider, externalSubject, cancellationToken)
            ?? throw new InvalidOperationException("User insert conflicted but no existing row was found.");
        return new ProvisionedUser(winner, IsNew: false);
    }

    private async Task PublishUserRegisteredAsync(
        User user,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var @event = new UserRegistered(
            user.Id,
            user.Provider.ToString().ToLowerInvariant(),
            user.PhoneNumber,
            user.Email,
            now);

        try
        {
            await _eventPublisher.PublishAsync(@event, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to publish UserRegistered for {UserId}. The DB row is committed; reconciliation required.",
                user.Id);
            // Do not rethrow — the user is already persisted; surfacing this as 5xx would
            // confuse the client. A future spec will add an outbox-based publisher.
        }
    }
}
