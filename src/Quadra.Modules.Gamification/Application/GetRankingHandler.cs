using System.Globalization;
using Quadra.Modules.Gamification.Contracts;
using Quadra.Modules.Gamification.Entities;
using Quadra.Modules.Gamification.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Gamification.Application;

/// <summary>
/// Season rankings (DECISIONS #63, #95): points from recurring matches played in a season, among
/// the caller's network, inside one recurring group, or among everyone, optionally only players
/// of one court position (from their profile).
/// </summary>
public sealed class GetRankingHandler
{
    /// <summary>Games in the season a player needs to appear in the general ranking.</summary>
    public const int GeneralMinMatches = 3;

    /// <summary>How far back the "moved up / down" arrow looks.</summary>
    public static readonly TimeSpan TrendWindow = TimeSpan.FromDays(7);

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
        var now = _clock.GetUtcNow();
        var (seasonStart, seasonEnd) = query.Season is { } season ? Season(season) : CurrentSeason(now);

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
            userIds = (await _following.GetNetworkIdsAsync(callerId, cancellationToken)).Append(callerId).ToArray();
        }

        var totals = await _points.GetTotalsSinceAsync(seasonStart, seasonEnd, groupId, userIds, cancellationToken);

        // ponytail: ranks the whole season in memory (one summary per scoring player); move the
        // position filter and paging into SQL once a season has thousands of players.
        var players = await _players.GetSummariesAsync(totals.Select(t => t.UserId).ToArray(), cancellationToken);
        var minMatches = query.Scope == RankingScopes.General ? GeneralMinMatches : 1;
        var board = Rank(totals, players, query.Position, minMatches, query.Sort);

        // The arrow: the same board a week ago, while that is still inside this season.
        var weekAgo = now - TrendWindow;
        if (weekAgo > seasonStart && now < seasonEnd)
        {
            var before = await _points.GetTotalsSinceAsync(seasonStart, weekAgo, groupId, userIds, cancellationToken);
            var previous = Rank(before, players, query.Position, minMatches, query.Sort)
                .ToDictionary(e => e.UserId, e => e.Rank);
            board = board.Select(e => e with { PreviousRank = previous.GetValueOrDefault(e.UserId) }).ToList();
        }

        var callerIndex = board.ToList().FindIndex(e => e.UserId == callerId);
        var callerEntry = callerIndex >= 0 ? board[callerIndex] : null;
        if (callerEntry is null
            && totals.FirstOrDefault(t => t.UserId == callerId) is { } mine
            && (query.Position is null || players[callerId].Position == query.Position))
        {
            callerEntry = Entry(null, mine, players[callerId]);
        }

        // Who to pass: the closest line ranked strictly above the caller (the last one when unranked).
        var nextUp = callerIndex >= 0
            ? board.Take(callerIndex).LastOrDefault(e => e.Rank < callerEntry!.Rank)
            : callerEntry is null ? null : board.LastOrDefault();

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
            (long)query.Page * query.PageSize < board.Count,
            query.Sort,
            nextUp);
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

    /// <summary>What one match gave the caller, or null when it gave them nothing.</summary>
    public async Task<MatchPointsResponse?> GetMatchPointsAsync(Guid callerId, Guid matchId, CancellationToken cancellationToken)
    {
        var rows = await _points.GetMatchAwardsAsync(callerId, matchId, cancellationToken);
        if (rows.Count == 0)
        {
            return null;
        }

        int Sum(Func<PointTransaction, bool> pick, Func<PointTransaction, int> value) => rows.Where(pick).Sum(value);
        bool IsWin(PointTransaction t) => t.Reason is PointReason.GameWin or PointReason.Win;
        bool IsMvp(PointTransaction t) => t.Reason is PointReason.MvpVote or PointReason.Mvp;

        return new MatchPointsResponse(
            matchId,
            rows[0].GroupId,
            rows.Sum(t => t.Points),
            Sum(t => t.Reason == PointReason.Attendance, t => t.Points),
            Sum(IsWin, t => t.Points),
            Sum(IsMvp, t => t.Points),
            Sum(IsWin, t => t.Quantity),
            Sum(t => t.Reason == PointReason.MvpVote, t => t.Quantity),
            rows.Any(t => t.Reason == PointReason.Mvp));
    }

    /// <summary>
    /// The calendar month in Brasília time: a season starts on day 1 at 00:00 and ends when the
    /// next one starts.
    /// </summary>
    public static (DateTimeOffset Start, DateTimeOffset End) CurrentSeason(DateTimeOffset now)
    {
        var local = TimeZoneInfo.ConvertTime(now, Brasilia);
        return Season(new DateTime(local.Year, local.Month, 1));
    }

    /// <summary>The season named <c>yyyy-MM</c> (validated by <c>RankingQueryValidator</c>).</summary>
    public static (DateTimeOffset Start, DateTimeOffset End) Season(string season) =>
        Season(DateTime.ParseExact(season, "yyyy-MM", CultureInfo.InvariantCulture));

    private static (DateTimeOffset Start, DateTimeOffset End) Season(DateTime first)
    {
        var next = first.AddMonths(1);
        return (new DateTimeOffset(first, Brasilia.GetUtcOffset(first)), new DateTimeOffset(next, Brasilia.GetUtcOffset(next)));
    }

    /// <summary>
    /// Orders the qualifying players. By points: points, MVPs, votes, games won, fewer matches.
    /// By MVPs: MVPs, votes, then the points order. By wins: games won, then the points order.
    /// Players level on every key share a rank (1, 2, 2, 4).
    /// </summary>
    public static IReadOnlyList<RankingEntryResponse> Rank(
        IEnumerable<SeasonTotals> totals,
        IReadOnlyDictionary<Guid, PlayerSummary> players,
        string? position,
        int minMatches,
        string sort = RankingSorts.Points)
    {
        var qualifying = totals
            .Where(t => t.Matches >= minMatches)
            .Where(t => position is null || players[t.UserId].Position == position);

        var ordered = (sort switch
            {
                RankingSorts.Mvps => qualifying.OrderByDescending(t => t.Mvps).ThenByDescending(t => t.Votes).ThenByDescending(t => t.Points),
                RankingSorts.Wins => qualifying.OrderByDescending(t => t.Wins).ThenByDescending(t => t.Points),
                _ => qualifying.OrderByDescending(t => t.Points),
            })
            .ThenByDescending(t => t.Mvps)
            .ThenByDescending(t => t.Votes)
            .ThenByDescending(t => t.Wins)
            .ThenBy(t => t.Matches)
            .ThenBy(t => t.UserId)
            .ToList();

        var board = new List<RankingEntryResponse>(ordered.Count);
        for (var i = 0; i < ordered.Count; i++)
        {
            var t = ordered[i];
            var tied = i > 0 && ordered[i - 1] is var p
                && (p.Points, p.Mvps, p.Votes, p.Wins, p.Matches) == (t.Points, t.Mvps, t.Votes, t.Wins, t.Matches);
            board.Add(Entry(tied ? board[i - 1].Rank : i + 1, t, players[t.UserId]));
        }

        return board;
    }

    private static RankingEntryResponse Entry(int? rank, SeasonTotals t, PlayerSummary p) =>
        new(rank, t.UserId, p.DisplayName, p.Handle, p.Position, p.Level, p.PhotoUrl, t.Points, t.Matches, t.Wins, t.Mvps, p.Overall,
            t.Votes, t.AttendancePoints, t.WinPoints, t.MvpPoints);
}
