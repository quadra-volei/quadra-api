using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Retrieves the current team composition for a match.
/// </summary>
public sealed class GetTeamsHandler
{
    private readonly IMatchReader _matchReader;
    private readonly ITeamRepository _teamRepository;
    private readonly IPlayerLevelReader _playerLevelReader;

    public GetTeamsHandler(
        IMatchReader matchReader,
        ITeamRepository teamRepository,
        IPlayerLevelReader playerLevelReader)
    {
        _matchReader = matchReader;
        _teamRepository = teamRepository;
        _playerLevelReader = playerLevelReader;
    }

    public async Task<TeamsResponse> HandleAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        // 1. Verify match exists.
        var matchSummary = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        _ = matchSummary; // match exists; its full data is not needed beyond existence check here.

        // 2. Load teams.
        var teams = await _teamRepository.ListByMatchAsync(matchId, cancellationToken);
        if (teams.Count == 0)
        {
            throw new TeamsNotFoundException(matchId);
        }

        // 3. Retrieve player levels.
        var allPlayerIds = teams
            .SelectMany(t => t.PlayerIds)
            .ToList();

        var levelMap = await _playerLevelReader.GetPlayerLevelsAsync(allPlayerIds, cancellationToken);

        // 4. Map and return.
        return DraftTeamsHandler.MapToResponse(teams, levelMap);
    }
}
