using Quadra.Modules.Gamification.Contracts;
using Quadra.Modules.Gamification.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Gamification.Application;

/// <summary>
/// The ranking the caller is part of, without having to say which match: the group
/// (recurring match) where they scored most recently, with each player's identity resolved
/// through Profile. Null when the caller has not scored in any group yet.
/// </summary>
public sealed class GetMyRankingHandler
{
    private readonly IGroupRankingRepository _rankings;
    private readonly GetGroupRankingHandler _groupRanking;
    private readonly IPlayerSummaryReader _players;

    public GetMyRankingHandler(
        IGroupRankingRepository rankings,
        GetGroupRankingHandler groupRanking,
        IPlayerSummaryReader players)
    {
        _rankings = rankings;
        _groupRanking = groupRanking;
        _players = players;
    }

    public async Task<GroupRankingResponse?> HandleAsync(
        Guid callerId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // In the MVP a group is a single recurring match: GroupId == its match id.
        if (await _rankings.FindLatestGroupIdAsync(callerId, cancellationToken) is not { } groupId)
        {
            return null;
        }

        GroupRankingResponse ranking;
        try
        {
            ranking = await _groupRanking.HandleAsync(groupId, callerId, page, pageSize, cancellationToken);
        }
        catch (MatchNotFoundForRankingException)
        {
            return null; // the match behind the group is gone
        }

        var ids = ranking.Items.Select(i => i.UserId).Append(callerId).Distinct().ToArray();
        var summaries = await _players.GetSummariesAsync(ids, cancellationToken);
        GroupRankingEntryResponse Named(GroupRankingEntryResponse entry) =>
            summaries.TryGetValue(entry.UserId, out var player)
                ? entry with
                {
                    DisplayName = player.DisplayName,
                    Handle = player.Handle,
                    Position = player.Position,
                    PhotoUrl = player.PhotoUrl,
                }
                : entry;

        return ranking with
        {
            Items = ranking.Items.Select(Named).ToList(),
            CallerEntry = ranking.CallerEntry is null ? null : Named(ranking.CallerEntry),
        };
    }
}
