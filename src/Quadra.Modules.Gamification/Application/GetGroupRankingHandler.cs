using Quadra.Modules.Gamification.Contracts;
using Quadra.Modules.Gamification.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Gamification.Application;

/// <summary>
/// Serves the accumulated point ranking for a recurring match group. Validates the match through
/// <see cref="IMatchGroupReader"/> (Matches, read-only), then reads only Gamification-owned tables
/// for the standings. No cross-module JOIN and no InGame access at read time.
/// </summary>
public sealed class GetGroupRankingHandler
{
    private readonly IMatchGroupReader _matchGroupReader;
    private readonly IGroupRankingRepository _rankings;

    public GetGroupRankingHandler(
        IMatchGroupReader matchGroupReader,
        IGroupRankingRepository rankings)
    {
        _matchGroupReader = matchGroupReader;
        _rankings = rankings;
    }

    public async Task<GroupRankingResponse> HandleAsync(
        Guid matchId,
        Guid callerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var descriptor = await _matchGroupReader.GetMatchGroupAsync(matchId, cancellationToken);
        if (descriptor is null)
        {
            throw new MatchNotFoundForRankingException(matchId);
        }

        if (!string.Equals(descriptor.Type, "Recurring", StringComparison.Ordinal))
        {
            throw new RankingNotAvailableForOneOffException();
        }

        var groupId = descriptor.GroupId;

        var pageRows = await _rankings.GetPageAsync(groupId, page, pageSize, cancellationToken);
        var totalCount = await _rankings.CountByGroupAsync(groupId, cancellationToken);

        var items = new List<GroupRankingEntryResponse>(pageRows.Count);
        for (var i = 0; i < pageRows.Count; i++)
        {
            var row = pageRows[i];
            var rank = ((page - 1) * pageSize) + i + 1;
            items.Add(new GroupRankingEntryResponse(
                rank,
                row.UserId,
                row.TotalPoints,
                row.MatchesCounted,
                row.LastMatchDateTime));
        }

        GroupRankingEntryResponse? callerEntry = null;
        var callerRow = await _rankings.FindEntryAsync(groupId, callerId, cancellationToken);
        if (callerRow is not null)
        {
            var callerRank = await _rankings.GetRankAsync(groupId, callerId, cancellationToken);
            callerEntry = new GroupRankingEntryResponse(
                callerRank ?? 0,
                callerRow.UserId,
                callerRow.TotalPoints,
                callerRow.MatchesCounted,
                callerRow.LastMatchDateTime);
        }

        var hasNextPage = (long)page * pageSize < totalCount;

        return new GroupRankingResponse(
            groupId,
            descriptor.Name,
            items,
            callerEntry,
            page,
            pageSize,
            totalCount,
            hasNextPage);
    }
}
