namespace Quadra.Modules.Gamification.Contracts;

/// <summary>One player's standing within a group ranking.</summary>
public sealed record GroupRankingEntryResponse(
    int Rank,                                       // 1-based absolute position; ties broken by UserId ascending
    Guid UserId,
    int TotalPoints,
    int MatchesCounted,
    DateTimeOffset? LastMatchDateTime);
