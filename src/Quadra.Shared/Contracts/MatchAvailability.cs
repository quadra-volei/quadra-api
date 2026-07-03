namespace Quadra.Shared.Contracts;

/// <summary>
/// Per-match slot-occupancy projection returned by <see cref="IMatchAvailabilityReader"/>.
/// </summary>
public sealed record MatchAvailability(
    Guid MatchId,
    int MaxPlayers,
    int ConfirmedCount,        // confirmed presences (Regular + DropIn)
    int OpenSlots,             // Math.Max(0, MaxPlayers - ConfirmedCount)
    bool HasOpenDropInSlot);   // true when a DropIn can still be confirmed per Matches slot rules
