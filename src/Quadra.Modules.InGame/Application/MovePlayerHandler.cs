using Microsoft.Extensions.Logging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Moves a player from their current team to a specified target team within a match.
/// </summary>
public sealed class MovePlayerHandler
{
    private readonly IMatchReader _matchReader;
    private readonly ITeamRepository _teamRepository;
    private readonly IPlayerLevelReader _playerLevelReader;
    private readonly ILogger<MovePlayerHandler> _logger;

    public MovePlayerHandler(
        IMatchReader matchReader,
        ITeamRepository teamRepository,
        IPlayerLevelReader playerLevelReader,
        ILogger<MovePlayerHandler> logger)
    {
        _matchReader = matchReader;
        _teamRepository = teamRepository;
        _playerLevelReader = playerLevelReader;
        _logger = logger;
    }

    public async Task<TeamsResponse> HandleAsync(
        Guid matchId,
        Guid teamId,
        Guid playerId,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        // 1. Verify match exists.
        var matchSummary = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // 2. Verify caller is organizer (handled in controller — exception just in case).
        if (matchSummary.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        // 3. Verify match is Closed.
        if (matchSummary.Status != "Closed")
        {
            throw new InvalidMatchStatusForTeamsException(
                "Manual team adjustments are only allowed while the match is Closed.");
        }

        // 4. Load teams.
        var teams = await _teamRepository.ListByMatchAsync(matchId, cancellationToken);
        if (teams.Count == 0)
        {
            throw new TeamsNotFoundException(matchId);
        }

        // 5. Verify target team exists.
        var targetTeam = teams.FirstOrDefault(t => t.Id == teamId)
            ?? throw new TeamNotFoundException(teamId, matchId);

        // 6. Find the member row for this player across all teams.
        var member = teams
            .SelectMany(t => t.Members)
            .FirstOrDefault(m => m.PlayerId == playerId)
            ?? throw new PlayerNotInTeamException(playerId, matchId);

        // 7. No-op if player is already in target team.
        if (member.TeamId == teamId)
        {
            var allPlayerIds = teams
                .SelectMany(t => t.Members)
                .Select(m => m.PlayerId)
                .ToList();
            var currentLevelMap = await _playerLevelReader.GetPlayerLevelsAsync(allPlayerIds, cancellationToken);
            return DraftTeamsHandler.MapToResponse(teams, currentLevelMap);
        }

        // 8. Persist the move.
        await _teamRepository.MovePlayerAsync(member.Id, teamId, cancellationToken);

        _logger.LogInformation(
            "Player moved in MatchId={MatchId}. PlayerId={PlayerId}, TargetTeamId={TargetTeamId}.",
            matchId, playerId, teamId);

        // 9. Reload and return updated state.
        var updatedTeams = await _teamRepository.ListByMatchAsync(matchId, cancellationToken);
        var playerIds = updatedTeams
            .SelectMany(t => t.Members)
            .Select(m => m.PlayerId)
            .ToList();
        var levelMap = await _playerLevelReader.GetPlayerLevelsAsync(playerIds, cancellationToken);

        _ = targetTeam; // captured for validation; no further use needed.

        return DraftTeamsHandler.MapToResponse(updatedTeams, levelMap);
    }
}
