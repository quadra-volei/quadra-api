namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Thrown when no summary has been generated for the requested match.
/// </summary>
public sealed class MatchSummaryNotFoundException : Exception
{
    public MatchSummaryNotFoundException(Guid matchId)
        : base($"No summary has been generated for match '{matchId}' yet.") { }
}

/// <summary>
/// Thrown when a summary already exists for the match (the record is immutable and cannot be regenerated).
/// </summary>
public sealed class MatchSummaryAlreadyExistsException : Exception
{
    public MatchSummaryAlreadyExistsException(Guid matchId)
        : base($"A summary has already been generated for match '{matchId}' and cannot be regenerated.") { }
}

/// <summary>
/// Thrown when the summary cannot be generated because the match's game has not ended yet,
/// or the match is in a state (e.g. cancelled) that cannot be summarized.
/// </summary>
public sealed class MatchNotEndedException : Exception
{
    public MatchNotEndedException(string message)
        : base(message) { }
}

/// <summary>
/// Thrown when the summary cannot be generated because the MVP voting session is still open.
/// </summary>
public sealed class MvpVotingStillOpenException : Exception
{
    public MvpVotingStillOpenException(string message)
        : base(message) { }
}

/// <summary>
/// Thrown when no scoreboard exists for the match, so no result can be snapshotted.
/// </summary>
public sealed class ScoreboardNotFoundForSummaryException : Exception
{
    public ScoreboardNotFoundForSummaryException(string message)
        : base(message) { }
}
