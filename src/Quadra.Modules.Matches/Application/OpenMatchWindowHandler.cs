using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Events.Matches;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Transitions a match from <c>Draft</c> to <c>Open</c> and publishes <see cref="MatchWindowOpened"/>.
/// Called by the Background Worker when <c>WindowOpensAt</c> is reached.
/// </summary>
public sealed class OpenMatchWindowHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenMatchWindowHandler> _logger;

    public OpenMatchWindowHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        ILogger<OpenMatchWindowHandler> logger)
    {
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        var now = _timeProvider.GetUtcNow();

        // Throws InvalidMatchStatusTransitionException if not Draft.
        match.OpenWindow(now);

        await _matchRepository.UpdateAsync(match, cancellationToken);

        _logger.LogInformation(
            "Match window opened. MatchId={MatchId} Status={Status}",
            matchId, match.Status);

        // Gather all pending players to notify.
        var presences = await _presenceRepository.ListByMatchAsync(matchId, cancellationToken);
        var pendingPlayerIds = presences
            .Where(p => p.Status == PresenceStatus.Pending)
            .Select(p => p.PlayerId)
            .ToList();

        var @event = new MatchWindowOpened(
            MatchId: match.Id,
            OrganizerId: match.OrganizerId,
            WindowClosesAt: match.WindowClosesAt,
            PendingPlayerIds: pendingPlayerIds,
            OccurredAt: now);

        await _eventPublisher.PublishAsync(@event, cancellationToken);
    }
}
