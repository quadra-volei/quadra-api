namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Thrown when the caller has no presence row for the requested match.
/// </summary>
public sealed class PresenceNotFoundException : Exception
{
    public PresenceNotFoundException(Guid matchId, Guid playerId)
        : base($"Presence for player '{playerId}' in match '{matchId}' was not found.") { }
}

/// <summary>
/// Thrown when a player already has a presence row for the requested match.
/// </summary>
public sealed class PresenceAlreadyExistsException : Exception
{
    public PresenceAlreadyExistsException(Guid matchId, Guid playerId)
        : base($"Player '{playerId}' already has a presence row for match '{matchId}'.") { }
}

/// <summary>
/// Thrown when the confirmation window is not open for the player's type.
/// </summary>
public sealed class PresenceWindowNotOpenException : Exception
{
    public PresenceWindowNotOpenException(string message)
        : base(message) { }
}

/// <summary>
/// Thrown when all slots of the player's type are taken.
/// Contains the waiting list position assigned to the player.
/// </summary>
public sealed class SlotLimitReachedException : Exception
{
    public int WaitingListPosition { get; }
    public bool AlreadyOnWaitingList { get; }

    public SlotLimitReachedException(int waitingListPosition)
        : base("All slots are full. You have been added to the waiting list.")
    {
        WaitingListPosition = waitingListPosition;
        AlreadyOnWaitingList = false;
    }

    public SlotLimitReachedException()
        : base("All slots are full; you are already on the waiting list.")
    {
        WaitingListPosition = 0;
        AlreadyOnWaitingList = true;
    }
}
