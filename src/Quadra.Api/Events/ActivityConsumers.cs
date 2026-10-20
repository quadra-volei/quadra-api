using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Profile.Activities;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.InGame;
using Quadra.Shared.Events.Matches;
using Quadra.Shared.Events.Profile;

namespace Quadra.Api.Events;

/// <summary>
/// Turns game events into "Atividade dos amigos" rows (Profile's <see cref="ActivityWriter"/>):
/// the game started (each player on a team), finished (each participant, with their result and
/// sets), its MVP, and a new level number. A private match never produces an activity. Events
/// arrive at least once; the writer keeps one row per player and event.
/// </summary>
public sealed class ActivityConsumers :
    IEventHandler<MatchStarted>,
    IEventHandler<MatchSummaryGenerated>,
    IEventHandler<MvpAwarded>,
    IEventHandler<PlayerLevelUp>
{
    private readonly IMatchAccessReader _matchAccess;
    private readonly IMatchDescriptorReader _descriptors;
    private readonly IMatchResultReader _results;
    private readonly ActivityWriter _writer;

    public ActivityConsumers(
        IMatchAccessReader matchAccess,
        IMatchDescriptorReader descriptors,
        IMatchResultReader results,
        ActivityWriter writer)
    {
        _matchAccess = matchAccess;
        _descriptors = descriptors;
        _results = results;
        _writer = writer;
    }

    public async Task HandleAsync(MatchStarted @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var descriptor = await OpenMatchAsync(@event.MatchId, cancellationToken);
        var result = await _results.GetMatchResultAsync(@event.MatchId, cancellationToken);
        if (descriptor is null || result is null)
        {
            return;
        }

        foreach (var playerId in result.Teams.SelectMany(t => t.PlayerIds).Distinct())
        {
            await _writer.RecordAsync(
                new ActivityRecord(playerId, ActivityType.MatchStarted, @event.StartedAt, @event.MatchId, descriptor.Name),
                cancellationToken);
        }
    }

    public async Task HandleAsync(MatchSummaryGenerated @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        var descriptor = await OpenMatchAsync(@event.MatchId, cancellationToken);
        var result = await _results.GetMatchResultAsync(@event.MatchId, cancellationToken);
        if (descriptor is null || result is null)
        {
            return;
        }

        foreach (var p in MatchSummaryGeneratedProfileConsumer.ToParticipations(@event, descriptor, result))
        {
            await _writer.RecordAsync(
                new ActivityRecord(
                    p.PlayerId,
                    ActivityType.MatchEnded,
                    @event.GeneratedAt,
                    @event.MatchId,
                    descriptor.Name,
                    p.Outcome,
                    p.SetsWon,
                    p.SetsLost),
                cancellationToken);
        }
    }

    public async Task HandleAsync(MvpAwarded @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (await OpenMatchAsync(@event.MatchId, cancellationToken) is not { } descriptor)
        {
            return;
        }

        await _writer.RecordAsync(
            new ActivityRecord(@event.PlayerId, ActivityType.MvpAwarded, @event.AwardedAt, @event.MatchId, descriptor.Name),
            cancellationToken);
    }

    public Task HandleAsync(PlayerLevelUp @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return _writer.RecordAsync(
            new ActivityRecord(@event.PlayerId, ActivityType.LevelUp, @event.OccurredAt, LevelFrom: @event.FromLevel, LevelTo: @event.ToLevel),
            cancellationToken);
    }

    /// <summary>The match's descriptor when it exists and is open; null otherwise (no activity).</summary>
    private async Task<MatchDescriptor?> OpenMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var open = await _matchAccess.ListPublicAsync([matchId], cancellationToken);
        return open.Contains(matchId)
            ? await _descriptors.GetMatchDescriptorAsync(matchId, cancellationToken)
            : null;
    }
}
