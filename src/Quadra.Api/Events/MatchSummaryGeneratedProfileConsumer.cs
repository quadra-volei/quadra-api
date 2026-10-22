using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Profile.Abstractions;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.Matches;

namespace Quadra.Api.Events;

/// <summary>
/// Consumes the <see cref="MatchSummaryGenerated"/> event — the single authoritative "finished
/// match" signal for Profile. For each participant it composes a boundary-safe
/// <see cref="FinishedMatchParticipation"/> (enriched via <see cref="IMatchDescriptorReader"/> for
/// name/date and <see cref="IMatchResultReader"/> for team + outcome) and hands it to the
/// Profile-owned <see cref="IPlayerStatsWriter"/>. Idempotent under at-least-once delivery.
/// </summary>
public sealed class MatchSummaryGeneratedProfileConsumer : IEventHandler<MatchSummaryGenerated>
{
    private readonly IMatchDescriptorReader _matchDescriptorReader;
    private readonly IMatchResultReader _matchResultReader;
    private readonly IPlayerStatsWriter _statsWriter;
    private readonly ILogger<MatchSummaryGeneratedProfileConsumer> _logger;

    public MatchSummaryGeneratedProfileConsumer(
        IMatchDescriptorReader matchDescriptorReader,
        IMatchResultReader matchResultReader,
        IPlayerStatsWriter statsWriter,
        ILogger<MatchSummaryGeneratedProfileConsumer> logger)
    {
        _matchDescriptorReader = matchDescriptorReader;
        _matchResultReader = matchResultReader;
        _statsWriter = statsWriter;
        _logger = logger;
    }

    public async Task HandleAsync(MatchSummaryGenerated @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        var descriptor = await _matchDescriptorReader.GetMatchDescriptorAsync(@event.MatchId, cancellationToken);
        var result = await _matchResultReader.GetMatchResultAsync(@event.MatchId, cancellationToken);

        if (descriptor is null || result is null)
        {
            _logger.LogWarning(
                "Skipping profile update for match {MatchId}: descriptor or result unavailable.",
                @event.MatchId);
            return;
        }

        foreach (var participation in ToParticipations(@event, descriptor, result))
        {
            await _statsWriter.ApplyFinishedMatchAsync(participation, cancellationToken);
        }

        _logger.LogInformation(
            "Applied finished match {MatchId} to {ParticipantCount} participant profile(s).",
            @event.MatchId,
            @event.ParticipantPlayerIds.Count);
    }

    /// <summary>Each participant's side of the finished match: team, outcome and sets won and lost.</summary>
    internal static IEnumerable<FinishedMatchParticipation> ToParticipations(
        MatchSummaryGenerated @event,
        MatchDescriptor descriptor,
        MatchResult result)
    {
        // Map each participant to the team they played on (from the InGame result snapshot).
        var teamByPlayer = new Dictionary<Guid, Guid>();
        foreach (var team in result.Teams)
        {
            foreach (var playerId in team.PlayerIds)
            {
                teamByPlayer[playerId] = team.TeamId;
            }
        }

        // Sets won per team across the game (teams may rotate, so not just the last pair).
        var setsByTeam = result.Sets
            .Where(s => s.WinnerTeamId is not null)
            .GroupBy(s => s.WinnerTeamId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        // No fixed teams: one roster, no score, nobody wins or loses (DECISIONS #86).
        var free = result.Format == "Free";
        var actionsByPlayer = (result.PlayerStats ?? []).ToDictionary(s => s.PlayerId);

        foreach (var playerId in @event.ParticipantPlayerIds)
        {
            Guid? teamId = teamByPlayer.TryGetValue(playerId, out var resolvedTeamId)
                ? resolvedTeamId
                : null;
            var actions = actionsByPlayer.GetValueOrDefault(playerId);

            yield return new FinishedMatchParticipation(
                PlayerId: playerId,
                MatchId: @event.MatchId,
                MatchName: descriptor.Name,
                MatchDateTime: descriptor.DateTime,
                TeamId: teamId,
                Outcome: free ? "NoResult" : DetermineOutcome(teamId, result.WinnerTeamId),
                WasMvp: @event.MvpPlayerId == playerId,
                DurationSeconds: @event.DurationSeconds,
                FinishedAt: @event.GeneratedAt,
                Format: descriptor.Format,
                SetsWon: teamId is { } mine && !free ? setsByTeam.GetValueOrDefault(mine) : null,
                SetsLost: teamId is { } own && !free
                    ? setsByTeam.Where(t => t.Key != own).Select(t => t.Value).DefaultIfEmpty(0).Max()
                    : null,
                Actions: actions is null
                    ? null
                    : new PlayerMatchActions(actions.Kills, actions.Blocks, actions.Aces, actions.Digs, actions.Assists),
                RalliesPlayed: teamId is { } onCourt ? RalliesPlayed(result.Sets, onCourt) : null,
                PlannedMinutes: descriptor.DurationMinutes);
        }
    }

    /// <summary>Points played in the sets the team was on court for; null when no set says who played it.</summary>
    internal static int? RalliesPlayed(IEnumerable<MatchResultSet> sets, Guid teamId)
    {
        var played = sets.Where(s => s.TeamAId == teamId || s.TeamBId == teamId).ToList();
        return played.Count == 0 ? null : played.Sum(s => s.TeamAPoints + s.TeamBPoints);
    }

    private static string DetermineOutcome(Guid? teamId, Guid? winnerTeamId)
    {
        if (winnerTeamId is null)
        {
            return "Draw";
        }

        return teamId is not null && teamId == winnerTeamId ? "Win" : "Loss";
    }
}
