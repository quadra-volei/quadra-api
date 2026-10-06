namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Thrown when a match with the requested identifier does not exist.
/// </summary>
public sealed class MatchNotFoundException : Exception
{
    public MatchNotFoundException(Guid matchId)
        : base($"Match '{matchId}' was not found.") { }
}

/// <summary>
/// Thrown when the caller attempts to modify a match they do not own.
/// </summary>
public sealed class MatchAccessDeniedException : Exception
{
    public MatchAccessDeniedException(Guid matchId, Guid callerId)
        : base($"Caller '{callerId}' is not the organizer of match '{matchId}'.") { }
}

/// <summary>
/// Thrown when a status transition is not permitted by the match state machine.
/// </summary>
public sealed class InvalidMatchStatusTransitionException : Exception
{
    public InvalidMatchStatusTransitionException(string message)
        : base(message) { }
}

/// <summary>
/// The caller may not join this private match: no (or a wrong) invite code, or the match only
/// accepts players the organizer adds. Maps to HTTP 403.
/// </summary>
public sealed class MatchInviteRequiredException : Exception
{
    public MatchInviteRequiredException(Guid matchId)
        : base($"Match '{matchId}' is private; a valid invite is required to join.") { }
}

/// <summary>Every slot of the match is taken. Maps to HTTP 409.</summary>
public sealed class MatchFullException : Exception
{
    public MatchFullException(Guid matchId)
        : base($"Match '{matchId}' has no free slot.") { }
}

/// <summary>The guest does not exist in this match. Maps to HTTP 404.</summary>
public sealed class MatchGuestNotFoundException : Exception
{
    public MatchGuestNotFoundException(Guid matchId, Guid guestId)
        : base($"Guest '{guestId}' was not found in match '{matchId}'.") { }
}
