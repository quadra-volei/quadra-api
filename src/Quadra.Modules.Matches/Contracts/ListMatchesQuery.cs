namespace Quadra.Modules.Matches.Contracts;

/// <summary>
/// Query parameters for <c>GET /api/v1/matches</c>.
/// </summary>
public sealed record ListMatchesQuery(
    Guid? OrganizerId,
    string? Status,
    int Page = 1,
    int PageSize = 20);
