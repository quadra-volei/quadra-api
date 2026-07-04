namespace Quadra.Modules.Gamification.Contracts;

/// <summary>Query parameters for the group-ranking endpoint.</summary>
public sealed record GroupRankingQuery(int Page = 1, int PageSize = 20);
