using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Orchestrates match cancellation: loads, authorizes, transitions state, persists, and publishes.
/// </summary>
public sealed class CancelMatchHandler
{
    private readonly IMatchRepository _repository;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;

    public CancelMatchHandler(
        IMatchRepository repository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider)
    {
        _repository = repository;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
    }

    public async Task HandleAsync(Guid matchId, Guid callerId, CancellationToken cancellationToken)
    {
        var match = await _repository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        if (match.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        var previousStatus = match.Status;
        var now = _timeProvider.GetUtcNow();

        // Cancel() throws InvalidMatchStatusTransitionException for InProgress/Ended.
        match.Cancel(now);

        await _repository.UpdateAsync(match, cancellationToken);

        var @event = new MatchStatusChanged(
            MatchId: match.Id,
            OrganizerId: match.OrganizerId,
            PreviousStatus: previousStatus.ToString(),
            NewStatus: match.Status.ToString(),
            OccurredAt: now);

        await _eventPublisher.PublishAsync(@event, cancellationToken);
    }
}
