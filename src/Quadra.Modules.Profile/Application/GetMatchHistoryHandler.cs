using Quadra.Modules.Profile.Contracts;
using Quadra.Modules.Profile.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Profile.Application;

/// <summary>
/// Returns a player's match history, newest first, paginated. Serves both the <c>/me</c> and the
/// <c>/{userId}</c> variants. Another player's history hides the matches the caller may not see
/// (CanViewMatch): the entry stays, so counts add up, but without the match's name or id.
/// </summary>
public sealed class GetMatchHistoryHandler
{
    public const string HiddenMatchName = "Partida privada";

    private readonly IPlayerProfileRepository _profiles;
    private readonly IPlayerMatchHistoryRepository _history;
    private readonly IMatchAccessReader _matchAccess;

    public GetMatchHistoryHandler(
        IPlayerProfileRepository profiles,
        IPlayerMatchHistoryRepository history,
        IMatchAccessReader matchAccess)
    {
        _profiles = profiles;
        _history = history;
        _matchAccess = matchAccess;
    }

    public async Task<PagedResponse<MatchHistoryEntryResponse>> HandleAsync(
        Guid userId,
        Guid callerId,
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

        var viewable = userId == callerId
            ? null
            : await _matchAccess.ListViewableAsync(items.Select(h => h.MatchId).ToList(), callerId, cancellationToken);

        var mapped = items
            .Select(h => new MatchHistoryEntryResponse(
                viewable is null || viewable.Contains(h.MatchId) ? h.MatchId : Guid.Empty,
                viewable is null || viewable.Contains(h.MatchId) ? h.MatchName : HiddenMatchName,
                h.MatchDateTime,
                h.TeamId,
                h.Outcome.ToString(),
                h.WasMvp,
                h.DurationSeconds,
                h.Format,
                h.SetsWon,
                h.SetsLost,
                h.ExposureGames is null ? null : new MatchActionsResponse(h.Kills, h.Blocks, h.Aces, h.Digs, h.Assists)))
            .ToList();

        var hasNextPage = (long)page * pageSize < totalCount;

        return new PagedResponse<MatchHistoryEntryResponse>(mapped, page, pageSize, totalCount, hasNextPage);
    }
}
