using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Realtime;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// What happens when a set is over, shared by every way a set can end (the winning point or
/// the organizer ending it early).
/// </summary>
internal static class ScoreboardProgression
{
    /// <summary>
    /// Finishes <paramref name="set"/> for <paramref name="winnerTeamId"/>. The game ends when
    /// that team reaches the sets the format asks for. Otherwise a two-team game opens the next
    /// set by itself, while a game with rotating teams waits for the organizer to choose who
    /// plays next (<see cref="OpenNextSetHandler"/>).
    /// </summary>
    /// <returns>True when the game ended.</returns>
    public static bool CompleteSet(Scoreboard scoreboard, ScoreboardSet set, Guid winnerTeamId, DateTimeOffset now)
    {
        set.Finish(winnerTeamId, now);
        scoreboard.RegisterSetWon(winnerTeamId);

        var setsToWin = VolleyballScoringRules.SetsToWin(scoreboard.Format);
        var winnerSets = scoreboard.RotatesTeams
            ? scoreboard.SetsWonBy(winnerTeamId)
            : Math.Max(scoreboard.TeamASetsWon, scoreboard.TeamBSetsWon);
        if (winnerSets >= setsToWin)
        {
            scoreboard.End(winnerTeamId, now);
            return true;
        }

        if (!scoreboard.RotatesTeams)
        {
            var nextSetNumber = scoreboard.CurrentSetNumber + 1;
            scoreboard.OpenSet(
                nextSetNumber,
                VolleyballScoringRules.IsDecidingSet(scoreboard.Format, nextSetNumber),
                now);
        }

        return false;
    }
}

/// <summary>Dependencies and guards common to the organizer's set operations.</summary>
public abstract class ScoreboardSetHandlerBase
{
    private readonly IMatchReader _matchReader;

    protected ScoreboardSetHandlerBase(
        IMatchReader matchReader,
        IScoreboardRepository scoreboardRepository,
        IMatchRoomNotifier matchRoomNotifier,
        TimeProvider timeProvider)
    {
        _matchReader = matchReader;
        Scoreboards = scoreboardRepository;
        Notifier = matchRoomNotifier;
        Clock = timeProvider;
    }

    protected IScoreboardRepository Scoreboards { get; }

    protected IMatchRoomNotifier Notifier { get; }

    protected TimeProvider Clock { get; }

    /// <summary>The in-progress scoreboard of a match the caller organizes.</summary>
    protected async Task<Scoreboard> LoadInProgressAsync(Guid matchId, Guid callerId, CancellationToken cancellationToken)
    {
        var match = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);
        if (match.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        var scoreboard = await Scoreboards.FindByMatchAsync(matchId, cancellationToken)
            ?? throw new ScoreboardNotFoundException();
        if (scoreboard.State != ScoreboardState.InProgress)
        {
            throw new InvalidScoreboardStateException("The game is not in progress.");
        }

        return scoreboard;
    }

    /// <summary>The set being played; it must be <paramref name="setNumber"/> and still open.</summary>
    protected static ScoreboardSet OpenCurrentSet(Scoreboard scoreboard, int setNumber)
    {
        if (setNumber != scoreboard.CurrentSetNumber)
        {
            throw new SetNotCurrentException();
        }

        var set = scoreboard.Sets.First(s => s.SetNumber == setNumber);
        if (set.Status == SetStatus.Finished)
        {
            throw new InvalidScoreboardStateException("This set is already finished.");
        }

        return set;
    }

    protected async Task<ScoreboardResponse> SaveAndBroadcastAsync(
        Scoreboard scoreboard,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await Scoreboards.UpdateAsync(scoreboard, cancellationToken);
        await Notifier.NotifyScoreboardUpdatedAsync(
            GetScoreboardHandler.ToRealtimeMessage(scoreboard, now), cancellationToken);
        return GetScoreboardHandler.MapToResponse(scoreboard);
    }
}

/// <summary>
/// Takes back the last point of the current set (a mis-tap). One level only: a point that
/// already ended the set cannot be undone.
/// </summary>
public sealed class UndoPointHandler : ScoreboardSetHandlerBase
{
    public UndoPointHandler(
        IMatchReader matchReader,
        IScoreboardRepository scoreboardRepository,
        IMatchRoomNotifier matchRoomNotifier,
        TimeProvider timeProvider)
        : base(matchReader, scoreboardRepository, matchRoomNotifier, timeProvider)
    {
    }

    public async Task<ScoreboardResponse> HandleAsync(
        Guid matchId,
        int setNumber,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        var scoreboard = await LoadInProgressAsync(matchId, callerId, cancellationToken);
        var set = OpenCurrentSet(scoreboard, setNumber);
        if (!set.UndoLastPoint(scoreboard.TeamAId, scoreboard.TeamBId))
        {
            throw new InvalidScoreboardStateException("There is no point to undo.");
        }

        return await SaveAndBroadcastAsync(scoreboard, Clock.GetUtcNow(), cancellationToken);
    }
}

/// <summary>
/// Ends the current set before the target score: whoever is ahead takes it. This is how a
/// pickup game plays shorter sets (to 15, by time…) without configuring a target.
/// </summary>
public sealed class EndSetHandler : ScoreboardSetHandlerBase
{
    private readonly IEventPublisher _eventPublisher;
    private readonly ILogger<EndSetHandler> _logger;

    public EndSetHandler(
        IMatchReader matchReader,
        IScoreboardRepository scoreboardRepository,
        IEventPublisher eventPublisher,
        IMatchRoomNotifier matchRoomNotifier,
        TimeProvider timeProvider,
        ILogger<EndSetHandler> logger)
        : base(matchReader, scoreboardRepository, matchRoomNotifier, timeProvider)
    {
        _eventPublisher = eventPublisher;
        _logger = logger;
    }

    public async Task<ScoreboardResponse> HandleAsync(
        Guid matchId,
        int setNumber,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        var scoreboard = await LoadInProgressAsync(matchId, callerId, cancellationToken);
        var set = OpenCurrentSet(scoreboard, setNumber);
        if (set.TeamAPoints == set.TeamBPoints)
        {
            throw new InvalidScoreboardStateException("A tied set cannot be ended: play one more point.");
        }

        var now = Clock.GetUtcNow();
        var winnerTeamId = set.TeamAPoints > set.TeamBPoints ? scoreboard.TeamAId : scoreboard.TeamBId;
        var gameEnded = ScoreboardProgression.CompleteSet(scoreboard, set, winnerTeamId, now);

        _logger.LogInformation(
            "Set {SetNumber} ended early for MatchId={MatchId}. WinnerTeamId={WinnerTeamId}, GameEnded={GameEnded}.",
            setNumber, matchId, winnerTeamId, gameEnded);

        var response = await SaveAndBroadcastAsync(scoreboard, now, cancellationToken);
        if (gameEnded)
        {
            await _eventPublisher.PublishAsync(
                RecordPointHandler.BuildMatchEndedEvent(scoreboard, now), cancellationToken);
        }

        return response;
    }
}

/// <summary>
/// Opens the next set between the two teams the organizer picked — the "winner stays, next
/// team comes in" rotation of a game with more than two teams.
/// </summary>
public sealed class OpenNextSetHandler : ScoreboardSetHandlerBase
{
    private readonly ITeamRepository _teamRepository;

    public OpenNextSetHandler(
        IMatchReader matchReader,
        IScoreboardRepository scoreboardRepository,
        ITeamRepository teamRepository,
        IMatchRoomNotifier matchRoomNotifier,
        TimeProvider timeProvider)
        : base(matchReader, scoreboardRepository, matchRoomNotifier, timeProvider)
    {
        _teamRepository = teamRepository;
    }

    public async Task<ScoreboardResponse> HandleAsync(
        Guid matchId,
        Guid teamAId,
        Guid teamBId,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        var scoreboard = await LoadInProgressAsync(matchId, callerId, cancellationToken);
        if (scoreboard.Sets.Any(s => s.Status == SetStatus.InProgress))
        {
            throw new InvalidScoreboardStateException("Finish the current set before opening the next one.");
        }

        await EnsureTeamsOfMatchAsync(_teamRepository, matchId, teamAId, teamBId, cancellationToken);

        var now = Clock.GetUtcNow();
        scoreboard.SetPair(teamAId, teamBId);
        var oneAway = VolleyballScoringRules.SetsToWin(scoreboard.Format) - 1;
        scoreboard.OpenSet(
            scoreboard.CurrentSetNumber + 1,
            isDecidingSet: scoreboard.TeamASetsWon == oneAway && scoreboard.TeamBSetsWon == oneAway,
            now);

        return await SaveAndBroadcastAsync(scoreboard, now, cancellationToken);
    }

    /// <summary>Both ids must be different teams of the match.</summary>
    internal static async Task EnsureTeamsOfMatchAsync(
        ITeamRepository teamRepository,
        Guid matchId,
        Guid teamAId,
        Guid teamBId,
        CancellationToken cancellationToken)
    {
        var teamIds = (await teamRepository.ListByMatchAsync(matchId, cancellationToken))
            .Select(t => t.Id)
            .ToHashSet();
        if (teamAId == teamBId || !teamIds.Contains(teamAId) || !teamIds.Contains(teamBId))
        {
            throw new TeamNotInScoreboardException();
        }
    }
}
