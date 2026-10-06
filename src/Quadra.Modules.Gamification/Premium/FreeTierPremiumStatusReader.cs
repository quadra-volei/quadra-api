using Quadra.Shared.Contracts;

namespace Quadra.Modules.Gamification.Premium;

/// <summary>
/// MVP stub implementation of <see cref="IPremiumStatusReader"/>. Every player is treated as free
/// tier — billing is out of MVP scope (PRODUCT: monetization is validation-only), so no persistent
/// premium data structure exists yet. When real premium is specified this reader is replaced; the
/// contract stays the same, so consumers such as the F2.2 player-card read path are unaffected.
/// </summary>
public sealed class FreeTierPremiumStatusReader : IPremiumStatusReader
{
    /// <inheritdoc/>
    public Task<bool> IsPremiumAsync(Guid userId, CancellationToken cancellationToken)
    {
        return Task.FromResult(false);
    }
}
