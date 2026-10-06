namespace Quadra.Modules.Profile.Contracts;

/// <summary>Query parameters for the match-history endpoints.</summary>
public sealed record MatchHistoryQuery(int Page = 1, int PageSize = 20);
