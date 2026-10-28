using Microsoft.Extensions.Logging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Moves a player from their current team to a specified target team within a match. The bench
/// (confirmed players and guests in no team: who sat out of the draw) is a place too: a player on
/// it can be put into a team, and <paramref name="teamId"/> null sends a member back to it.
/// </summary>
public sealed class MovePlayerHandler
{
    private readonly IMatchReader _matchReader;
    private readonly ITeamRepository _teamRepository;
    private readonly IScoreboardRepository _scoreboardRepository;
    private readonly IPlayerLevelReader _playerLevelReader;
    private readonly IConfirmedPlayersReader _confirmedPlayersReader;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<MovePlayerHandler> _logger;

    public MovePlayerHandler(
        IMatchReader matchReader,
        ITeamRepository teamRepository,
        IScoreboardRepository scoreboardRepository,
        IPlayerLevelReader playerLevelReader,
        IConfirmedPlayersReader confirmedPlayersReader,
        TimeProvider timeProvider,
        ILogger<MovePlayerHandler> logger)
    {
        _confirmedPlayersReader = confirmedPlayersReader;
        _timeProvider = timeProvider;
        _matchReader = matchReader;
        _teamRepository = teamRepository;
        _scoreboardRepository = scoreboardRepository;
        _playerLevelReader = playerLevelReader;
        _logger = logger;
    }

    public async Task<TeamsResponse> HandleAsync(
        Guid matchId,
        Guid? teamId,
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

        // 3. Same window as the draw: confirmations open, game not started.
        InvalidMatchStatusForTeamsException.ThrowUnlessEditable(
            matchSummary.Status, await _scoreboardRepository.FindByMatchAsync(matchId, cancellationToken));

        // 4. Load teams.
        var teams = await _teamRepository.ListByMatchAsync(matchId, cancellationToken);
        if (teams.Count == 0)
        {
            throw new TeamsNotFoundException(matchId);
        }

        // 5. Verify target team exists (none: the bench).
        var targetTeam = teamId is { } targetId
            ? teams.FirstOrDefault(t => t.Id == targetId) ?? throw new TeamNotFoundException(targetId, matchId)
            : null;

        // 6. Find the member row for this player across all teams; none means they are on the
        //    bench, which only confirmed players and guests of the match can be.
        var member = teams
            .SelectMany(t => t.Members)
            .FirstOrDefault(m => m.PlayerId == playerId);
        if (member is null)
        {
            var guests = await _confirmedPlayersReader.GetGuestIdsAsync(matchId, cancellationToken) ?? [];
            var isGuest = guests.Contains(playerId);
            if (targetTeam is null
                || (!isGuest && !(await _confirmedPlayersReader.GetConfirmedPlayerIdsAsync(matchId, cancellationToken)).Contains(playerId)))
            {
                throw new PlayerNotInTeamException(playerId, matchId);
            }

            await _teamRepository.AddMemberAsync(
                TeamMember.Create(targetTeam.Id, matchId, playerId, _timeProvider.GetUtcNow(), isGuest), cancellationToken);
            _logger.LogInformation(
                "Player put in from the bench in MatchId={MatchId}. PlayerId={PlayerId}, TargetTeamId={TargetTeamId}.",
                matchId, playerId, targetTeam.Id);
            return await ReloadAsync(matchId, cancellationToken);
        }

        if (targetTeam is null)
        {
            await _teamRepository.RemoveMemberAsync(member.Id, cancellationToken);
            _logger.LogInformation(
                "Player sent to the bench in MatchId={MatchId}. PlayerId={PlayerId}.", matchId, playerId);
            return await ReloadAsync(matchId, cancellationToken);
        }

        // 7. No-op if player is already in target team.
        if (member.TeamId == targetTeam.Id)
        {
            var allPlayerIds = teams
                .SelectMany(t => t.Members)
                .Select(m => m.PlayerId)
                .ToList();
            var currentLevelMap = await _playerLevelReader.GetPlayerLevelsAsync(allPlayerIds, cancellationToken);
            return DraftTeamsHandler.MapToResponse(teams, currentLevelMap);
        }

        // 8. Persist the move.
        await _teamRepository.MovePlayerAsync(member.Id, targetTeam.Id, cancellationToken);

        _logger.LogInformation(
            "Player moved in MatchId={MatchId}. PlayerId={PlayerId}, TargetTeamId={TargetTeamId}.",
            matchId, playerId, teamId);

        // 9. Reload and return updated state.
        return await ReloadAsync(matchId, cancellationToken);
    }

    private async Task<TeamsResponse> ReloadAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var updatedTeams = await _teamRepository.ListByMatchAsync(matchId, cancellationToken);
        var playerIds = updatedTeams
            .SelectMany(t => t.Members)
            .Select(m => m.PlayerId)
            .ToList();
        var levelMap = await _playerLevelReader.GetPlayerLevelsAsync(playerIds, cancellationToken);
        return DraftTeamsHandler.MapToResponse(updatedTeams, levelMap);
    }
}
