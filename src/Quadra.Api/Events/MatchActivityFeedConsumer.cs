using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Profile.Abstractions;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.Matches;

namespace Quadra.Api.Events;

/// <summary>
/// Puts the automatic activity cards in the "Rede" feed: "created the match" on
/// <see cref="MatchCreated"/> and "confirmed presence" on <see cref="PresenceConfirmed"/>.
/// Only for a match a post could invite to (<see cref="IMatchInviteReader"/>: open to everyone
/// and still to come), so a private match never shows up in the feed. The organizer confirming
/// in their own match adds nothing: the "created" card already says they are in.
/// </summary>
public sealed class MatchActivityFeedConsumer : IEventHandler<MatchCreated>, IEventHandler<PresenceConfirmed>
{
    private readonly IMatchInviteReader _matchInvites;
    private readonly IActivityFeedWriter _feed;

    public MatchActivityFeedConsumer(IMatchInviteReader matchInvites, IActivityFeedWriter feed)
    {
        _matchInvites = matchInvites;
        _feed = feed;
    }

    public Task HandleAsync(MatchCreated @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return AddAsync(@event.OrganizerId, MatchActivity.Created, @event.MatchId, cancellationToken);
    }

    public Task HandleAsync(PresenceConfirmed @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return AddAsync(@event.PlayerId, MatchActivity.Confirmed, @event.MatchId, cancellationToken);
    }

    private async Task AddAsync(Guid playerId, MatchActivity activity, Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _matchInvites.FindInvitableAsync(matchId, playerId, cancellationToken);
        if (match is null || (activity == MatchActivity.Confirmed && match.OrganizerId == playerId))
        {
            return;
        }

        await _feed.AddMatchActivityAsync(playerId, activity, matchId, match.Name, match.DateTime, cancellationToken);
    }
}
