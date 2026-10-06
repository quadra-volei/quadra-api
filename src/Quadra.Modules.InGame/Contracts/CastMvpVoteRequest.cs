namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Request to cast (or replace, while Open) the caller's single MVP vote.
/// </summary>
public sealed record CastMvpVoteRequest(
    Guid VotedPlayerId);
