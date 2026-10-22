using Quadra.Modules.Gamification.Contracts;
using Quadra.Modules.Gamification.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Gamification.Application;

/// <summary>
/// Season rankings (DECISIONS #63): points from recurring matches played this season, among the
/// caller and the players they follow, inside one recurring group, or among everyone, optionally
/// only players of one court position (from their profile).
/// </summary>
public sealed class GetRankingHandler
{
    /// <summary>Games in the season a player needs to appear in the general ranking.</summary>
    public const int GeneralMinMatches = 3;

    private static readonly TimeZoneInfo Brasilia = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    private readonly IPointTransactionRepository _points;
    private readonly IFollowingReader _following;
    private readonly IPlayerSummaryReader _players;
    private readonly IMatchAccessReader _access;
    private readonly IMatchGroupReader _groups;
    private readonly TimeProvider _clock;

    public GetRankingHandler(
        IPointTransactionRepository points,
        IFollowingReader following,
        IPlayerSummaryReader players,
        IMatchAccessReader access,
        IMatchGroupReader groups,
        TimeProvider clock)
    {
        _points = points;
        _following = following;
        _players = players;
        _access = access;
        _groups = groups;
        _clock = clock;
    }

    /// <returns>The ranking, or null when the group does not exist or the caller may not see it.</returns>
    public async Task<RankingResponse?> HandleAsync(Guid callerId, RankingQuery query, CancellationToken cancellationToken)
    {
        var (seasonStart, seasonEnd) = CurrentSeason(_clock.GetUtcNow());

        string? groupName = null;
        Guid? groupId = null;
        IReadOnlyCollection<Guid>? userIds = null;
        if (query.Scope == RankingScopes.Group)
        {
            // groupId may be any game of the group (the app sends the game it shows). The caller
            // sees the ranking when they may see that game or have scored in the group.
            var matchId = query.GroupId!.Value;
            var group = await _groups.GetMatchGroupAsync(matchId, cancellationToken);
            if (group is null
                || (!await _access.CanViewAsync(matchId, callerId, cancellationToken)
                    && !(await _points.GetGroupIdsAsync(callerId, cancellationToken)).Contains(group.GroupId)))
            {
                return null;
            }

            groupId = group.GroupId;
            groupName = group.Name;
        }
        else if (query.Scope == RankingScopes.Friends)
        {
            userIds = (await _following.GetFollowingIdsAsync(callerId, cancellationToken)).Append(callerId).ToArray();
        }

        var totals = await _points.GetTotalsSinceAsync(
            seasonStart,
            groupId,
            userIds,
            cancellationToken);

        // ponytail: ranks the whole season in memory (one summary per scoring player); move the
        // position filter and paging into SQL once a season has thousands of players.
        var players = await _players.GetSummariesAsync(totals.Select(t => t.UserId).ToArray(), cancellationToken);
        var minMatches = query.Scope == RankingScopes.General ? GeneralMinMatches : 1;
        var board = Rank(totals, players, query.Position, minMatches);

        var callerEntry = board.FirstOrDefault(e => e.UserId == callerId);
        if (callerEntry is null
            && totals.FirstOrDefault(t => t.UserId == callerId) is { } mine
            && (query.Position is null || players[callerId].Position == query.Position))
        {
            callerEntry = Entry(null, mine, players[callerId]);
        }

        var items = board.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        return new RankingResponse(
            query.Scope,
            groupId,
            groupName,
            query.Position,
            seasonStart,
            seasonEnd,
            minMatches,
            items,
            callerEntry,
            query.Page,
            query.PageSize,
            board.Count,
            (long)query.Page * query.PageSize < board.Count);
    }

    /// <summary>The recurring games the caller scored in, most recent first, for the group picker.</summary>
    public async Task<IReadOnlyList<RankingGroupResponse>> ListGroupsAsync(Guid callerId, CancellationToken cancellationToken)
    {
        var result = new List<RankingGroupResponse>();
        foreach (var groupId in await _points.GetGroupIdsAsync(callerId, cancellationToken))
        {
            if (await _groups.GetMatchGroupAsync(groupId, cancellationToken) is { } group)
            {
                result.Add(new RankingGroupResponse(groupId, group.Name));
            }
        }

        return result;
    }

    /// <summary>
    /// The calendar month in Brasília time: a season starts on day 1 at 00:00 and ends when the
    /// next one starts.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) CurrentSeason(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Brasilia);
        var first = new DateTime(local.Year, local.Month, 1);
        var start = new DateTimeOffset(first, Brasilia.GetUtcOffset(first));
        var next = first.AddMonths(1);
        return (start, new DateTimeOffset(next, Brasilia.GetUtcOffset(next)));
    }

    /// <summary>
    /// Orders the qualifying players: points, then MVPs, then wins, then fewer games. Players
    /// level on all four share a rank (1, 2, 2, 4).
    /// </summary>
    public static IReadOnlyList<RankingEntryResponse> Rank(
        IEnumerable<SeasonTotals> totals,
        IReadOnlyDictionary<Guid, PlayerSummary> players,
        string? position,
        int minMatches)
    {
        var ordered = totals
            .Where(t => t.Matches >= minMatches)
            .Where(t => position is null || players[t.UserId].Position == position)
            .OrderByDescending(t => t.Points)
            .ThenByDescending(t => t.Mvps)
            .ThenByDescending(t => t.Wins)
            .ThenBy(t => t.Matches)
            .ThenBy(t => t.UserId)
            .ToList();

        var board = new List<RankingEntryResponse>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var t = ordered[i];
            var tied = i > 0 && ordered[i - 1] is var p
                && (p.Points, p.Mvps, p.Wins, p.Matches) == (t.Points, t.Mvps, t.Wins, t.Matches);
            board.Add(Entry(tied ? board[i - 1].Rank : i + 1, t, players[t.UserId]));
        }

        return board;
    }

    private static RankingEntryResponse Entry(int? rank, SeasonTotals t, PlayerSummary p) =>
        new(rank, t.UserId, p.DisplayName, p.Handle, p.Position, p.Level, p.PhotoUrl, t.Points, t.Matches, t.Wins, t.Mvps, p.Overall);
}
