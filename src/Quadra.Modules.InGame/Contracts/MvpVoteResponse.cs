namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Response DTO representing the caller's cast MVP vote.
/// </summary>
public sealed record MvpVoteResponse(
    Guid MatchId,
    Guid VoterPlayerId,
    Guid VotedPlayerId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
