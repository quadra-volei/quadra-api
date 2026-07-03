namespace Quadra.Shared.Contracts;

/// <summary>
/// Read-only cross-module query interface for a player's free/premium status.
/// Declared in <c>Quadra.Shared</c>; implemented in <c>Quadra.Modules.Gamification</c>.
/// The MVP ships a stub returning <c>false</c> for every player (billing is out of MVP scope —
/// PRODUCT states monetization is validation-only). The contract is stable, so activating real
/// premium later needs no change to consumers such as the player-card read path (F2.2).
/// </summary>
public interface IPremiumStatusReader
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="userId"/> is a premium player, <c>false</c> otherwise.
    /// </summary>
    Task<bool> IsPremiumAsync(Guid userId, CancellationToken cancellationToken);
}
