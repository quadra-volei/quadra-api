namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Thrown when no MVP voting session exists for the requested match.
/// </summary>
public sealed class MvpVotingNotFoundException : Exception
{
    public MvpVotingNotFoundException()
        : base("MVP voting has not been opened for this match.") { }
}

/// <summary>
/// Thrown when a voting session already exists for the match.
/// </summary>
public sealed class MvpVotingAlreadyExistsException : Exception
{
    public MvpVotingAlreadyExistsException()
        : base("An MVP voting session already exists for this match.") { }
}

/// <summary>
/// Thrown when an operation is requested against a voting session in an incompatible state.
/// The message carries the endpoint-specific detail.
/// </summary>
public sealed class InvalidMvpVotingStateException : Exception
{
    public InvalidMvpVotingStateException(string message)
        : base(message) { }
}

/// <summary>
/// Thrown when MVP voting is opened before the match's scoreboard has ended.
/// </summary>
public sealed class MatchNotEndedException : Exception
{
    public MatchNotEndedException()
        : base("MVP voting can only be opened after the match has ended.") { }
}

/// <summary>
/// Thrown when a caller attempts to vote for themselves.
/// </summary>
public sealed class SelfVoteNotAllowedException : Exception
{
    public SelfVoteNotAllowedException()
        : base("You cannot vote for yourself.") { }
}

/// <summary>
/// Thrown when a non-participant attempts to cast a vote.
/// </summary>
public sealed class NotAParticipantException : Exception
{
    public NotAParticipantException()
        : base("Only match participants can vote.") { }
}

/// <summary>
/// Thrown when the voted-for player is not a participant of the match.
/// </summary>
public sealed class VotedPlayerNotParticipantException : Exception
{
    public VotedPlayerNotParticipantException()
        : base("The voted player is not a participant of this match.") { }
}

/// <summary>
/// Thrown when a vote is cast after the voting deadline has passed.
/// </summary>
public sealed class VotingDeadlinePassedException : Exception
{
    public VotingDeadlinePassedException()
        : base("The voting deadline has passed.") { }
}
