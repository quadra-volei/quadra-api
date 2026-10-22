using Quadra.Modules.InGame.Entities;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// Read-only adapter that implements <see cref="IMatchResultReader"/> (declared in
/// <c>Quadra.Shared.Contracts</c>) by composing the InGame-owned scoreboard, team and MVP-voting
/// repositories. Exposes the finished-game result to the Matches module for its F1.6 summary
/// snapshot. Returns <c>null</c> when no scoreboard exists for the match.
/// </summary>
public sealed class MatchResultReader : IMatchResultReader
{
    private readonly IScoreboardRepository _scoreboardRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly IMvpVotingRepository _mvpVotingRepository;
    private readonly IPlayerMatchStatsRepository _statsRepository;

    public MatchResultReader(
        IScoreboardRepository scoreboardRepository,
        ITeamRepository teamRepository,
        IMvpVotingRepository mvpVotingRepository,
        IPlayerMatchStatsRepository statsRepository)
    {
        _statsRepository = statsRepository;
        _scoreboardRepository = scoreboardRepository;
        _teamRepository = teamRepository;
        _mvpVotingRepository = mvpVotingRepository;
    }

    /// <inheritdoc/>
    public async Task<MatchResult?> GetMatchResultAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var scoreboard = await _scoreboardRepository.FindByMatchAsync(matchId, cancellationToken);
        if (scoreboard is null)
        {
            return null;
        }

        var teams = await _teamRepository.ListByMatchAsync(matchId, cancellationToken);
        var voting = await _mvpVotingRepository.FindByMatchAsync(matchId, cancellationToken);

        var sets = scoreboard.Sets
            .OrderBy(s => s.SetNumber)
            .Select(s => new MatchResultSet(
                SetNumber: s.SetNumber,
                TeamAPoints: s.TeamAPoints,
                TeamBPoints: s.TeamBPoints,
                WinnerTeamId: s.WinnerTeamId,
                TeamAId: s.TeamAId,
                TeamBId: s.TeamBId))
            .ToList();

        var resultTeams = teams
            .OrderBy(t => TeamPalette.Order(t.Name))
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new MatchResultTeam(
                TeamId: t.Id,
                Name: t.Name,
                PlayerIds: t.PlayerIds.OrderBy(id => id).ToList()))
            .ToList();

        var mvpVotingState = voting is null ? "None" : voting.State.ToString();

        return new MatchResult(
            MatchId: matchId,
            ScoreboardState: scoreboard.State.ToString(),
            Format: scoreboard.Format.ToString(),
            TeamAId: scoreboard.TeamAId,
            TeamBId: scoreboard.TeamBId,
            TeamASetsWon: scoreboard.TeamASetsWon,
            TeamBSetsWon: scoreboard.TeamBSetsWon,
            WinnerTeamId: scoreboard.WinnerTeamId,
            StartedAt: scoreboard.StartedAt ?? default,
            EndedAt: scoreboard.EndedAt ?? default,
            Sets: sets,
            Teams: resultTeams,
            MvpVotingState: mvpVotingState,
            MvpPlayerId: voting?.MvpPlayerId,
            MvpVoteCount: voting?.MvpVoteCount,
            MvpTotalVotes: voting?.TotalVotes ?? 0,
            PlayerStats: ToResultStats(await _statsRepository.ListByMatchAsync(matchId, cancellationToken)));
    }

    /// <summary>
    /// An action nobody in the match has (all zero) reads as not recorded: the organizer did not
    /// track it, and it must not count as zero for anyone (DECISIONS #85).
    /// </summary>
    internal static IReadOnlyList<MatchResultPlayerStats> ToResultStats(IReadOnlyList<PlayerMatchStats> rows)
    {
        int? Tracked(int value, Func<PlayerMatchStats, int> action) => rows.Any(r => action(r) > 0) ? value : null;

        return rows
            .Select(r => new MatchResultPlayerStats(
                r.PlayerId,
                Tracked(r.Kills, s => s.Kills),
                Tracked(r.Blocks, s => s.Blocks),
                Tracked(r.Aces, s => s.Aces),
                Tracked(r.Digs, s => s.Digs),
                Tracked(r.Assists, s => s.Assists)))
            .ToList();
    }

    /// <inheritdoc/>
    public Task<IReadOnlySet<Guid>> ListEndedAsync(IReadOnlyCollection<Guid> matchIds, CancellationToken cancellationToken) =>
        _scoreboardRepository.ListEndedMatchIdsAsync(matchIds, cancellationToken);
}
