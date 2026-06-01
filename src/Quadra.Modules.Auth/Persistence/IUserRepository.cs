using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Persistence;

/// <summary>
/// Persistence operations on the local <c>users</c> table. Scoped lifetime.
/// </summary>
public interface IUserRepository
{
    Task<User?> FindByCognitoSubAsync(string cognitoSub, CancellationToken cancellationToken);

    Task<User?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken);

    Task AddAsync(User user, CancellationToken cancellationToken);
}
