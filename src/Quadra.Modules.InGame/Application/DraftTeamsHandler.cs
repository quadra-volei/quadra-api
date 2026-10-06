using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.InGame;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Orchestrates the team draw for a match: 2 to 4 teams, balanced by level (snake draft) or
/// random, with the match guests playing alongside the confirmed players.
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

    public const int MinTeams = 2;
    public const int MaxTeams = 4;

    public Task<TeamsResponse> HandleAsync(Guid matchId, Guid callerId, CancellationToken cancellationToken) =>
        HandleAsync(matchId, callerId, new DraftTeamsRequest(), cancellationToken);

    public async Task<TeamsResponse> HandleAsync(
        Guid matchId,
        Guid callerId,
        DraftTeamsRequest options,
        CancellationToken cancellationToken)
    {
        var teamCount = options.TeamCount ?? MinTeams;
        var random = string.Equals(options.Mode, "Random", StringComparison.OrdinalIgnoreCase);
        if (teamCount is < MinTeams or > MaxTeams
            || options.PerTeam is < 1
            || (options.Mode is not null && !random && !string.Equals(options.Mode, "Balanced", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDraftOptionsException();
        }

        // 1. Verify match exists.
        var matchSummary = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // 2. Verify caller is organizer (handled in controller — exception just in case).
        if (matchSummary.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        // 3. Confirmations must have opened (teams drawn while the window is still open only
        //    count who confirmed so far; drawing again replaces them).
        if (matchSummary.Status is not ("Open" or "Closed"))
        {
            throw new InvalidMatchStatusForTeamsException(
                "Teams can only be drafted once confirmations have opened and before the game is over.");
        }

        // 4. Load confirmed players.
        var playerIds = await _confirmedPlayersReader.GetConfirmedPlayerIdsAsync(matchId, cancellationToken);
        var guestIds = (await _confirmedPlayersReader.GetGuestIdsAsync(matchId, cancellationToken) ?? [])
            .ToHashSet();
        if (playerIds.Count + guestIds.Count == 0)
        {
            throw new NoConfirmedPlayersException(matchId);
        }

        // 5. Load player levels.
        var levelMap = await _playerLevelReader.GetPlayerLevelsAsync(playerIds, cancellationToken);

        // 6. Apply snake-draft algorithm.
        var now = _timeProvider.GetUtcNow();
        var teams = BuildDraft(matchId, playerIds, guestIds, levelMap, teamCount, options.PerTeam, random, now);

        // 7. Persist (delete existing + insert new) in a single transaction.
        await _teamRepository.ReplaceTeamsAsync(matchId, teams, cancellationToken);

        _logger.LogInformation(
            "Teams drafted for MatchId={MatchId}. Teams={TeamCount}, Sizes={Sizes}.",
            matchId, teams.Count, string.Join("/", teams.Select(t => t.Members.Count)));

        // 8. Publish event after commit.
        var @event = new TeamsFormed(
            MatchId: matchId,
            Teams: teams.Select(t => new TeamFormedEntry(
                TeamId: t.Id,
                TeamName: t.Name,
                PlayerIds: t.PlayerIds.ToList())).ToList(),
            OccurredAt: now);

        await _eventPublisher.PublishAsync(@event, cancellationToken);

        // 9. Map and return.
        return MapToResponse(teams, levelMap);
    }

    /// <summary>
    /// Deals the players into <paramref name="teamCount"/> teams ("Team A", "Team B", …).
    ///
    /// Balanced: players sorted by level descending (Elite=3 … Beginner=0, guests count as
    /// Beginner) with the id as a stable tie-break, dealt in a snake — A,B,B,A,… for two teams,
    /// A,B,C,C,B,A,… for three — so no team keeps the first pick of every round.
    /// Random: shuffled, then dealt the same way.
    /// With a per-team cap and more players than slots, who sits out is picked at random.
    /// </summary>
    private static IReadOnlyList<Team> BuildDraft(
        Guid matchId,
        IReadOnlyList<Guid> playerIds,
        IReadOnlySet<Guid> guestIds,
        IReadOnlyDictionary<Guid, string> levelMap,
        int teamCount,
        int? perTeam,
        bool random,
        DateTimeOffset now)
    {
        var everyone = playerIds.Concat(guestIds).Distinct().ToList();
        var slots = perTeam is { } size ? size * teamCount : everyone.Count;
        if (random || everyone.Count > slots)
        {
            Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(everyone));
        }

        var playing = everyone.Take(slots);
        var ordered = random
            ? playing.ToList()
            : playing
                .OrderByDescending(id => LevelScore(levelMap.GetValueOrDefault(id, "Beginner")))
                .ThenBy(id => id)
                .ToList();

        var teams = Enumerable.Range(0, teamCount)
            .Select(i => Team.Create(matchId, $"Team {(char)('A' + i)}", now))
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var round = i / teamCount;
            var position = i % teamCount;
            var target = teams[round % 2 == 0 ? position : teamCount - 1 - position];
            target.AddMember(ordered[i], now, isGuest: guestIds.Contains(ordered[i]));
        }

        return teams;
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
                    CreatedAt: m.CreatedAt,
                    IsGuest: m.IsGuest)).ToList(),
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
