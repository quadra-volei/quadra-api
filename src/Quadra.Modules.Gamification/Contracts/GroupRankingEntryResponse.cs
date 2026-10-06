namespace Quadra.Modules.Gamification.Contracts;

/// <summary>One player's standing within a group ranking.</summary>
public sealed record GroupRankingEntryResponse(
    int Rank,                                       // 1-based absolute position; ties broken by UserId ascending
    Guid UserId,
    int TotalPoints,
    int MatchesCounted,
    DateTimeOffset? LastMatchDateTime,
    // Who the player is — filled by GET /rankings/mine; null on the per-match endpoint.
    string? DisplayName = null,
    string? Handle = null,
    string? Position = null,
    string? PhotoUrl = null);
