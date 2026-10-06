using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Prepares the scoreboard for a match in <see cref="ScoreboardState.NotStarted"/> state.
/// </summary>
public sealed class CreateScoreboardHandler
{
    private readonly IMatchReader _matchReader;
    private readonly ITeamRepository _teamRepository;
    private readonly IScoreboardRepository _scoreboardRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CreateScoreboardHandler> _logger;

    public CreateScoreboardHandler(
        IMatchReader matchReader,
        ITeamRepository teamRepository,
        IScoreboardRepository scoreboardRepository,
        TimeProvider timeProvider,
        ILogger<CreateScoreboardHandler> logger)
    {
        _matchReader = matchReader;
        _teamRepository = teamRepository;
        _scoreboardRepository = scoreboardRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<ScoreboardResponse> HandleAsync(
        Guid matchId,
        Guid callerId,
        ScoreboardFormat format,
        CancellationToken cancellationToken,
        Guid? firstTeamAId = null,
        Guid? firstTeamBId = null)
    {
        // 1. Verify match exists.
        var matchSummary = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // 2. Verify caller is organizer.
        if (matchSummary.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        // 3. Match must be Closed (confirmation window closed and teams formed).
        if (matchSummary.Status is not ("Open" or "Closed"))
        {
            throw new InvalidScoreboardStateException(
                "A scoreboard can only be created once confirmations have opened and teams are formed.");
        }

        // 4. Load teams; must have the two formed teams.
        var teams = await _teamRepository.ListByMatchAsync(matchId, cancellationToken);
        if (teams.Count < 2)
        {
            throw new TeamsNotFormedException();
        }

        // 5. Reject if a scoreboard already exists for this match.
        var existing = await _scoreboardRepository.FindByMatchAsync(matchId, cancellationToken);
        if (existing is not null)
        {
            throw new ScoreboardAlreadyExistsException();
        }

        // 6. Deterministic team assignment: teams are ordered by name ascending by the repository.
        //    With more than two teams the organizer may say which pair plays the first set.
        var teamAId = firstTeamAId ?? teams[0].Id;
        var teamBId = firstTeamBId ?? teams[1].Id;
        if (teamAId == teamBId || teams.All(t => t.Id != teamAId) || teams.All(t => t.Id != teamBId))
        {
            throw new TeamNotInScoreboardException();
        }

        // 7. Create the aggregate in NotStarted state (no sets yet).
        var now = _timeProvider.GetUtcNow();
        var scoreboard = Scoreboard.Create(matchId, format, teamAId, teamBId, now, rotatesTeams: teams.Count > 2);

        // 8. Persist. The UNIQUE (match_id) constraint is a last-resort guard against races.
        try
        {
            await _scoreboardRepository.AddAsync(scoreboard, cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new ScoreboardAlreadyExistsException();
        }

        _logger.LogInformation(
            "Scoreboard created for MatchId={MatchId}. Format={Format}, State={State}.",
            matchId, scoreboard.Format, scoreboard.State);

        // 9. Map and return.
        return GetScoreboardHandler.MapToResponse(scoreboard);
    }
}
