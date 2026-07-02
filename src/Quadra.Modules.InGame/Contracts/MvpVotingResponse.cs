namespace Quadra.Modules.InGame.Contracts;

/// <summary>
/// Response DTO representing the current MVP voting state. Per-candidate tallies are populated
/// only once voting is <c>Closed</c> (to avoid influencing votes while <c>Open</c>).
/// </summary>
public sealed record MvpVotingResponse(
    Guid Id,
    Guid MatchId,
    string State,                          // Open | Closed
    DateTimeOffset DeadlineAt,
    int TotalVotes,
    bool CallerHasVoted,
    Guid? MvpPlayerId,                     // null until Closed with a decided MVP
    int? MvpVoteCount,
    IReadOnlyList<MvpVoteTally> Results,   // empty while Open; populated when Closed
    DateTimeOffset OpenedAt,
    DateTimeOffset? ClosedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
