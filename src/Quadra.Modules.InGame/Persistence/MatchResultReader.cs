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

    public MatchResultReader(
        IScoreboardRepository scoreboardRepository,
        ITeamRepository teamRepository,
        IMvpVotingRepository mvpVotingRepository)
    {
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
                WinnerTeamId: s.WinnerTeamId))
            .ToList();

        var resultTeams = teams
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new MatchResultTeam(
                TeamId: t.Id,
                Name: t.Name,
                PlayerIds: t.Members.Select(m => m.PlayerId).OrderBy(id => id).ToList()))
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
            MvpTotalVotes: voting?.TotalVotes ?? 0);
    }
}
