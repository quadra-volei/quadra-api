namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Thrown when no teams have been drafted yet for the requested match.
/// </summary>
public sealed class TeamsNotFoundException : Exception
{
    public TeamsNotFoundException(Guid matchId)
        : base($"Teams have not been formed for match '{matchId}' yet.") { }
}

/// <summary>
/// Thrown when the requested team ID does not exist for the given match.
/// </summary>
public sealed class TeamNotFoundException : Exception
{
    public TeamNotFoundException(Guid teamId, Guid matchId)
        : base($"Team '{teamId}' was not found for match '{matchId}'.") { }
}

/// <summary>
/// Thrown when the specified player is not a member of any team in the given match.
/// </summary>
public sealed class PlayerNotInTeamException : Exception
{
    public PlayerNotInTeamException(Guid playerId, Guid matchId)
        : base($"Player '{playerId}' is not a member of any team in match '{matchId}'.") { }
}

/// <summary>
/// Thrown when a team operation is requested but the match is not in the required status.
/// </summary>
public sealed class InvalidMatchStatusForTeamsException : Exception
{
    public InvalidMatchStatusForTeamsException(string message)
        : base(message) { }
}

/// <summary>
/// Thrown when a draft is requested but the match has no confirmed players.
/// </summary>
/// <summary>Thrown when the draft options are out of range (2–4 teams, at least 1 per team, known mode).</summary>
public sealed class InvalidDraftOptionsException : Exception
{
    public InvalidDraftOptionsException()
        : base("Teams must be 2 to 4, with at least 1 player per team, drawn Balanced or Random.") { }
}

public sealed class NoConfirmedPlayersException : Exception
{
    public NoConfirmedPlayersException(Guid matchId)
        : base($"No confirmed players found for match '{matchId}'.") { }
}
