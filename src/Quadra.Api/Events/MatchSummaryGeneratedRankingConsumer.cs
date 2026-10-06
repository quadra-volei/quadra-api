using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Gamification.Abstractions;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.Matches;

namespace Quadra.Api.Events;

/// <summary>
/// Consumes the <see cref="MatchSummaryGenerated"/> event — the single authoritative "finished
/// match" signal for Gamification. For a <b>Recurring</b> match only, it composes a boundary-safe
/// <see cref="FinishedMatchPoints"/> (grouping key + type via <see cref="IMatchGroupReader"/>; each
/// participant's team + win/loss via <see cref="IMatchResultReader"/>) and hands it to the
/// Gamification-owned <see cref="IMatchPointsWriter"/>. For a OneOff match it awards nothing.
/// Idempotent under at-least-once delivery.
/// </summary>
public sealed class MatchSummaryGeneratedRankingConsumer : IEventHandler<MatchSummaryGenerated>
{
    private readonly IMatchGroupReader _matchGroupReader;
    private readonly IMatchResultReader _matchResultReader;
    private readonly IMatchPointsWriter _pointsWriter;
    private readonly ILogger<MatchSummaryGeneratedRankingConsumer> _logger;

    public MatchSummaryGeneratedRankingConsumer(
        IMatchGroupReader matchGroupReader,
        IMatchResultReader matchResultReader,
        IMatchPointsWriter pointsWriter,
        ILogger<MatchSummaryGeneratedRankingConsumer> logger)
    {
        _matchGroupReader = matchGroupReader;
        _matchResultReader = matchResultReader;
        _pointsWriter = pointsWriter;
        _logger = logger;
    }

    public async Task HandleAsync(MatchSummaryGenerated @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var group = await _matchGroupReader.GetMatchGroupAsync(@event.MatchId, cancellationToken);
        if (group is null)
        {
            _logger.LogWarning(
                "Skipping ranking points for match {MatchId}: match descriptor unavailable.",
                @event.MatchId);
            return;
        }

        // Group ranking is only defined for recurring matches (SCOPE F2.3). OneOff matches award nothing.
        if (!string.Equals(group.Type, "Recurring", StringComparison.Ordinal))
        {
            _logger.LogInformation(
                "Skipping ranking points for match {MatchId}: match is {MatchType}, not Recurring.",
                @event.MatchId,
                group.Type);
            return;
        }

        var result = await _matchResultReader.GetMatchResultAsync(@event.MatchId, cancellationToken);
        if (result is null)
        {
            _logger.LogWarning(
                "Skipping ranking points for match {MatchId}: match result unavailable.",
                @event.MatchId);
            return;
        }

        // The participant set comes from the InGame team rosters (players who showed up). Each player's
        // Won flag is derived from whether their team is the winning team.
        var players = new List<FinishedMatchPlayerPoints>();
        foreach (var team in result.Teams)
        {
            var won = result.WinnerTeamId is not null && team.TeamId == result.WinnerTeamId;
            foreach (var playerId in team.PlayerIds)
            {
                players.Add(new FinishedMatchPlayerPoints(playerId, won));
            }
        }

        var points = new FinishedMatchPoints(
            MatchId: @event.MatchId,
            GroupId: group.GroupId,
            MatchDateTime: group.DateTime,
            MvpPlayerId: @event.MvpPlayerId,
            Players: players,
            FinishedAt: @event.GeneratedAt);

        await _pointsWriter.ApplyFinishedMatchPointsAsync(points, cancellationToken);

        _logger.LogInformation(
            "Applied ranking points for match {MatchId} in group {GroupId} to {PlayerCount} player(s).",
            @event.MatchId,
            group.GroupId,
            players.Count);
    }
}
