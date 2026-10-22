using Microsoft.Extensions.Logging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Application;

/// <summary>A player in the sheet did not play the match (not on a team, not confirmed).</summary>
public sealed class StatsPlayerNotParticipantException : Exception
{
    public StatsPlayerNotParticipantException()
        : base("Every player in the sheet must be a participant of the match.")
    {
    }
}

/// <summary>
/// The per-player actions sheet of a match (cortadas, blocks, aces, salvadas, assistências).
/// The organizer writes it once the game has started and until the summary is generated; after
/// that it is part of the finished match and never changes. Participants can read it.
/// </summary>
public sealed class PlayerStatsHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IScoreboardRepository _scoreboards;
    private readonly ITeamRepository _teams;
    private readonly IConfirmedPlayersReader _confirmed;
    private readonly IPlayerMatchStatsRepository _stats;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PlayerStatsHandler> _logger;

    public PlayerStatsHandler(
        IMatchReader matchReader,
        IScoreboardRepository scoreboards,
        ITeamRepository teams,
        IConfirmedPlayersReader confirmed,
        IPlayerMatchStatsRepository stats,
        TimeProvider timeProvider,
        ILogger<PlayerStatsHandler> logger)
    {
        _matchReader = matchReader;
        _scoreboards = scoreboards;
        _teams = teams;
        _confirmed = confirmed;
        _stats = stats;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<PlayerStatsResponse> GetAsync(Guid matchId, Guid callerId, CancellationToken cancellationToken)
    {
        var match = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);
        if (match.OrganizerId != callerId
            && !(await ParticipantIdsAsync(matchId, cancellationToken)).Contains(callerId))
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        var rows = await _stats.ListByMatchAsync(matchId, cancellationToken);
        return ToResponse(matchId, await IsEditableAsync(match, cancellationToken), rows);
    }

    public async Task<PlayerStatsResponse> SaveAsync(
        Guid matchId,
        Guid callerId,
        SavePlayerStatsRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var match = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);
        if (match.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        if (!await IsEditableAsync(match, cancellationToken))
        {
            throw new InvalidScoreboardStateException(
                "Stats can be recorded once the game has started and until the summary is generated.");
        }

        var participants = await ParticipantIdsAsync(matchId, cancellationToken);
        if (request.Players.Any(p => !participants.Contains(p.PlayerId)))
        {
            throw new StatsPlayerNotParticipantException();
        }

        var now = _timeProvider.GetUtcNow();
        var existing = (await _stats.ListByMatchAsync(matchId, cancellationToken)).ToDictionary(s => s.PlayerId);
        var rows = request.Players.Select(p =>
        {
            var row = existing.GetValueOrDefault(p.PlayerId) ?? PlayerMatchStats.Create(matchId, p.PlayerId, callerId, now);
            row.Set(p.Kills, p.Blocks, p.Aces, p.Digs, p.Assists, callerId, now);
            return row;
        }).ToList();

        await _stats.ReplaceAsync(matchId, rows, cancellationToken);
        _logger.LogInformation("Player stats saved for MatchId={MatchId}: {PlayerCount} player(s).", matchId, rows.Count);

        return ToResponse(matchId, editable: true, rows);
    }

    /// <summary>Started (a scoreboard past NotStarted) and not yet summarized nor cancelled.</summary>
    private async Task<bool> IsEditableAsync(MatchSummary match, CancellationToken cancellationToken)
    {
        if (match.Status is "Ended" or "Cancelled")
        {
            return false;
        }

        var scoreboard = await _scoreboards.FindByMatchAsync(match.Id, cancellationToken);
        return scoreboard is not null && scoreboard.State != ScoreboardState.NotStarted;
    }

    /// <summary>Who played: the team rosters and the confirmed list (a free match has one roster).</summary>
    private async Task<HashSet<Guid>> ParticipantIdsAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var teams = await _teams.ListByMatchAsync(matchId, cancellationToken);
        var confirmed = await _confirmed.GetConfirmedPlayerIdsAsync(matchId, cancellationToken);
        return teams.SelectMany(t => t.PlayerIds).Concat(confirmed).ToHashSet();
    }

    private static PlayerStatsResponse ToResponse(Guid matchId, bool editable, IEnumerable<PlayerMatchStats> rows) =>
        new(matchId, editable, rows
            .OrderBy(r => r.PlayerId)
            .Select(r => new PlayerActionsDto(r.PlayerId, r.Kills, r.Blocks, r.Aces, r.Digs, r.Assists))
            .ToList());
}
