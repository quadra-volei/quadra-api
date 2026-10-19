using Microsoft.Extensions.Logging;
using Quadra.Modules.Auth.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Auth.Application;

/// <summary>
/// The person's LGPD rights over their account (SEC-14): export everything every module keeps
/// about them, and delete the account. Deletion erases each module's data first and the account
/// last (its sessions go with it, so every token stops at once); a failure leaves the account in
/// place and the call can be repeated.
/// </summary>
public sealed class AccountDataHandler
{
    private readonly IUserRepository _users;
    private readonly IEnumerable<IPersonalDataStore> _stores;
    private readonly ILogger<AccountDataHandler> _logger;

    public AccountDataHandler(
        IUserRepository users,
        IEnumerable<IPersonalDataStore> stores,
        ILogger<AccountDataHandler> logger)
    {
        _users = users;
        _stores = stores;
        _logger = logger;
    }

    /// <summary>Null when the account does not exist.</summary>
    public async Task<IReadOnlyDictionary<string, object?>?> ExportAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await _users.FindByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var export = new Dictionary<string, object?>
        {
            ["account"] = new
            {
                user.Id,
                Provider = user.Provider.ToString().ToLowerInvariant(),
                user.PhoneNumber,
                user.Email,
                user.CreatedAt,
            },
        };
        foreach (var store in _stores)
        {
            export[store.Section] = await store.ExportAsync(userId, cancellationToken);
        }

        return export;
    }

    public async Task DeleteAsync(Guid userId, CancellationToken cancellationToken)
    {
        foreach (var store in _stores)
        {
            await store.EraseAsync(userId, cancellationToken);
        }

        await _users.DeleteAsync(userId, cancellationToken);
        _logger.LogInformation("Account deleted. {UserId}", userId);
    }
}
