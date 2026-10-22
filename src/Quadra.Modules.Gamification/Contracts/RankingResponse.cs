namespace Quadra.Modules.Gamification.Contracts;

/// <summary>Query parameters of <c>GET /api/v1/rankings</c>.</summary>
public sealed record RankingQuery(
    string Scope = RankingScopes.Friends,
    Guid? GroupId = null,
    string? Position = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>The scopes a ranking can have.</summary>
public static class RankingScopes
{
    public const string Friends = "friends";
    public const string Group = "group";
    public const string General = "general";
}

/// <summary>One player's line in a season ranking.</summary>
public sealed record RankingEntryResponse(
    int? Rank,                  // null for the caller when they do not qualify yet (fewer than MinMatches)
    Guid UserId,
    string DisplayName,
    string? Handle,
    string? Position,
    string Level,
    string? PhotoUrl,
    int Points,
    int Matches,
    int Wins,
    int Mvps,
    int? Overall = null);       // GERAL, the same number as the profile (DECISIONS #85)

/// <summary>A season ranking of one scope, optionally of one court position.</summary>
public sealed record RankingResponse(
    string Scope,
    Guid? GroupId,
    string? GroupName,
    string? Position,
    DateTimeOffset SeasonStart,
    DateTimeOffset SeasonEnd,
    int MinMatches,
    IReadOnlyList<RankingEntryResponse> Items,
    RankingEntryResponse? CallerEntry,
    int Page,
    int PageSize,
    long TotalCount,
    bool HasNextPage);

/// <summary>A recurring game the caller scored in, for the group picker.</summary>
public sealed record RankingGroupResponse(Guid GroupId, string Name);
