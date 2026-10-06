namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Thrown when no scoreboard exists for the requested match.
/// </summary>
public sealed class ScoreboardNotFoundException : Exception
{
    public ScoreboardNotFoundException()
        : base("Scoreboard not found for this match.") { }
}

/// <summary>
/// Thrown when a scoreboard already exists for the match.
/// </summary>
public sealed class ScoreboardAlreadyExistsException : Exception
{
    public ScoreboardAlreadyExistsException()
        : base("A scoreboard already exists for this match.") { }
}

/// <summary>
/// Thrown when an operation is requested against a scoreboard (or set) in an incompatible state.
/// The message carries the endpoint-specific detail.
/// </summary>
public sealed class InvalidScoreboardStateException : Exception
{
    public InvalidScoreboardStateException(string message)
        : base(message) { }
}

/// <summary>
/// Thrown when a scoreboard is requested but the two teams have not been formed for the match.
/// </summary>
public sealed class TeamsNotFormedException : Exception
{
    public TeamsNotFormedException()
        : base("Teams have not been formed for this match yet.") { }
}

/// <summary>
/// Thrown when a point is recorded for a team that is not part of the scoreboard.
/// </summary>
public sealed class TeamNotInScoreboardException : Exception
{
    public TeamNotInScoreboardException()
        : base("Team is not part of this scoreboard.") { }
}

/// <summary>
/// Thrown when a point is recorded for a set that is not the current set.
/// </summary>
public sealed class SetNotCurrentException : Exception
{
    public SetNotCurrentException()
        : base("Points can only be recorded for the current set.") { }
}
