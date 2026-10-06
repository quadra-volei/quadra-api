using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Persistence;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Returns a player's match history, newest first, paginated. Serves both the <c>/me</c> and the
/// <c>/{userId}</c> variants.
/// </summary>
public sealed class GetMatchHistoryHandler
{
    private readonly IPlayerProfileRepository _profiles;
    private readonly IPlayerMatchHistoryRepository _history;

    public GetMatchHistoryHandler(
        IPlayerProfileRepository profiles,
        IPlayerMatchHistoryRepository history)
    {
        _profiles = profiles;
        _history = history;
    }

    public async Task<PagedResponse<MatchHistoryEntryResponse>> HandleAsync(
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (!await _profiles.ExistsAsync(userId, cancellationToken))
        {
            throw new ProfileNotFoundException(userId);
        }

        var items = await _history.GetPageAsync(userId, page, pageSize, cancellationToken);
        var totalCount = await _history.CountByUserAsync(userId, cancellationToken);

        var mapped = items
            .Select(h => new MatchHistoryEntryResponse(
                h.MatchId,
                h.MatchName,
                h.MatchDateTime,
                h.TeamId,
                h.Outcome.ToString(),
                h.WasMvp,
                h.DurationSeconds,
                h.Format,
                h.SetsWon,
                h.SetsLost))
            .ToList();

        var hasNextPage = (long)page * pageSize < totalCount;

        return new PagedResponse<MatchHistoryEntryResponse>(mapped, page, pageSize, totalCount, hasNextPage);
    }
}
