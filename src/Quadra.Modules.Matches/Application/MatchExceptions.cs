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
