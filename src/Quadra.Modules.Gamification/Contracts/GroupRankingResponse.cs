namespace Quadra.Modules.Gamification.Contracts;

/// <summary>The accumulated point ranking for a recurring match group, ordered by total points desc.</summary>
public sealed record GroupRankingResponse(
    Guid GroupId,                                   // == the recurring match id in the MVP
    string MatchName,
    IReadOnlyList<GroupRankingEntryResponse> Items,
    GroupRankingEntryResponse? CallerEntry,         // caller's own standing; may be outside the page; null if none
    int Page,
    int PageSize,
    long TotalCount,
    bool HasNextPage);
