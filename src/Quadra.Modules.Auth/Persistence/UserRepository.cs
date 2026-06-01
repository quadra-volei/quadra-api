using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Auth.Entities;

namespace Quadra.Modules.Auth.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IUserRepository"/>. Caller is responsible for
/// committing the unit of work via <see cref="AuthDbContext.SaveChangesAsync(CancellationToken)"/>.
/// </summary>
public sealed class UserRepository : IUserRepository
{
    private readonly AuthDbContext _dbContext;

    public UserRepository(AuthDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<User?> FindByCognitoSubAsync(string cognitoSub, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cognitoSub);
        return _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.CognitoSub == cognitoSub, cancellationToken);
    }

    public Task<User?> FindByPhoneNumberAsync(string phoneNumber, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);
        return _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.PhoneNumber == phoneNumber, cancellationToken);
    }

    public async Task AddAsync(User user, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(user);
        await _dbContext.Users.AddAsync(user, cancellationToken).ConfigureAwait(false);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
