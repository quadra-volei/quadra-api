using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.InGame;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Orchestrates the automatic snake-draft team balancing for a match.
/// </summary>
public sealed class DraftTeamsHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IConfirmedPlayersReader _confirmedPlayersReader;
    private readonly IPlayerLevelReader _playerLevelReader;
    private readonly ITeamRepository _teamRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DraftTeamsHandler> _logger;

    public DraftTeamsHandler(
        IMatchReader matchReader,
        IConfirmedPlayersReader confirmedPlayersReader,
        IPlayerLevelReader playerLevelReader,
        ITeamRepository teamRepository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        ILogger<DraftTeamsHandler> logger)
    {
        _matchReader = matchReader;
        _confirmedPlayersReader = confirmedPlayersReader;
        _playerLevelReader = playerLevelReader;
        _teamRepository = teamRepository;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<TeamsResponse> HandleAsync(
        Guid matchId,
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
                "Teams can only be drafted after the confirmation window closes. Match must be in Closed status.");
        }

        // 4. Load confirmed players.
        var playerIds = await _confirmedPlayersReader.GetConfirmedPlayerIdsAsync(matchId, cancellationToken);
        if (playerIds.Count == 0)
        {
            throw new NoConfirmedPlayersException(matchId);
        }

        // 5. Load player levels.
        var levelMap = await _playerLevelReader.GetPlayerLevelsAsync(playerIds, cancellationToken);

        // 6. Apply snake-draft algorithm.
        var now = _timeProvider.GetUtcNow();
        var teams = BuildDraft(matchId, playerIds, levelMap, now);

        // 7. Persist (delete existing + insert new) in a single transaction.
        await _teamRepository.ReplaceTeamsAsync(matchId, teams, cancellationToken);

        _logger.LogInformation(
            "Teams drafted for MatchId={MatchId}. TeamACount={TeamACount}, TeamBCount={TeamBCount}.",
            matchId, teams[0].Members.Count, teams[1].Members.Count);

        // 8. Publish event after commit.
        var @event = new TeamsFormed(
            MatchId: matchId,
            Teams: teams.Select(t => new TeamFormedEntry(
                TeamId: t.Id,
                TeamName: t.Name,
                PlayerIds: t.Members.Select(m => m.PlayerId).ToList())).ToList(),
            OccurredAt: now);

        await _eventPublisher.PublishAsync(@event, cancellationToken);

        // 9. Map and return.
        return MapToResponse(teams, levelMap);
    }

    /// <summary>
    /// Applies the snake-draft balancing algorithm.
    /// Players are sorted by level descending (Elite=3, Advanced=2, Intermediate=1, Beginner=0),
    /// with a stable secondary sort on PlayerId for determinism in ties.
    /// Assignment: pick 1 → A, pick 2 → B, pick 3 → B, pick 4 → A, ...
    /// </summary>
    private static IReadOnlyList<Team> BuildDraft(
        Guid matchId,
        IReadOnlyList<Guid> playerIds,
        IReadOnlyDictionary<Guid, string> levelMap,
        DateTimeOffset now)
    {
        var sorted = playerIds
            .OrderByDescending(id => LevelScore(levelMap.GetValueOrDefault(id, "Beginner")))
            .ThenBy(id => id)
            .ToList();

        var teamA = Team.Create(matchId, "Team A", now);
        var teamB = Team.Create(matchId, "Team B", now);

        // Snake draft: A, B, B, A, A, B, B, A, ...
        // Round 0: A gets pick (0-indexed: 0), B gets picks 1
        // The pattern by pick index: 0→A, 1→B, 2→B, 3→A, 4→A, 5→B, 6→B, 7→A, ...
        // Expressed as: block = index / 2; if (block % 2 == 0) first pick of block → A else B
        // Simpler formula: position in round-of-2: (index / 2) % 2 == 0 means [A,B], else [B,A]
        for (var i = 0; i < sorted.Count; i++)
        {
            var blockIndex = i / 2;
            var positionInBlock = i % 2;
            Team target;
            if (blockIndex % 2 == 0)
            {
                // Even block: first slot → A, second slot → B
                target = positionInBlock == 0 ? teamA : teamB;
            }
            else
            {
                // Odd block: first slot → B, second slot → A
                target = positionInBlock == 0 ? teamB : teamA;
            }

            target.AddMember(sorted[i], now);
        }

        return [teamA, teamB];
    }

    private static int LevelScore(string level) => level switch
    {
        "Elite" => 3,
        "Advanced" => 2,
        "Intermediate" => 1,
        _ => 0, // "Beginner" and any unknown
    };

    internal static TeamsResponse MapToResponse(
        IReadOnlyList<Team> teams,
        IReadOnlyDictionary<Guid, string> levelMap)
    {
        return new TeamsResponse(
            Teams: teams.Select(t => new TeamResponse(
                Id: t.Id,
                MatchId: t.MatchId,
                Name: t.Name,
                Members: t.Members.Select(m => new TeamMemberResponse(
                    Id: m.Id,
                    TeamId: m.TeamId,
                    PlayerId: m.PlayerId,
                    PlayerLevel: levelMap.GetValueOrDefault(m.PlayerId, "Beginner"),
                    CreatedAt: m.CreatedAt)).ToList(),
                CreatedAt: t.CreatedAt,
                UpdatedAt: t.UpdatedAt)).ToList());
    }
}

/// <summary>Thrown when a match with the requested identifier does not exist.</summary>
public sealed class MatchNotFoundException : Exception
{
    public MatchNotFoundException(Guid matchId)
        : base($"Match '{matchId}' was not found.") { }
}

/// <summary>Thrown when the caller attempts to modify a match they do not own.</summary>
public sealed class MatchAccessDeniedException : Exception
{
    public MatchAccessDeniedException(Guid matchId, Guid callerId)
        : base($"Caller '{callerId}' is not the organizer of match '{matchId}'.") { }
}
