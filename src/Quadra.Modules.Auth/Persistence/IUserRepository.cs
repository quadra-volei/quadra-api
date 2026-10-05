using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Persistence;

/// <summary>
/// Persistence operations on the local <c>users</c> table. Scoped lifetime.
/// </summary>
public interface IUserRepository
{
    Task<User?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<User?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);

    Task<User?> FindByExternalIdentityAsync(
        IdentityProvider provider,
        string externalSubject,
        CancellationToken cancellationToken);

    /// <summary>
    /// Inserts the user and commits. Returns <c>false</c> — without inserting — when another
    /// request created the same phone number or external identity first (unique-index race).
    /// </summary>
    Task<bool> TryAddAsync(User user, CancellationToken cancellationToken);
}
