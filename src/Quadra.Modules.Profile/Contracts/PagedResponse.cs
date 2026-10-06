namespace Quadra.Modules.Profile.Contracts;

/// <summary>A generic page of results.</summary>
public sealed record PagedResponse<T>(
    IReadOnlyList<T> Items,
    int Page,
    int PageSize,
    long TotalCount,
    bool HasNextPage);
