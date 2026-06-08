namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Paginated response returned by <c>GET /api/v1/matches</c>.
/// </summary>
public sealed record PagedMatchesResponse(
    IReadOnlyList<MatchResponse> Items,
    int Page,
    int PageSize,
    int TotalCount);
