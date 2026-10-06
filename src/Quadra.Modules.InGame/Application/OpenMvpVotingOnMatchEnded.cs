using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Events.InGame;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Opens MVP voting as soon as the game ends, so players can vote without waiting for the
/// organizer to open it. Same default deadline as the manual open (24 hours).
/// </summary>
public sealed class OpenMvpVotingOnMatchEnded : IEventHandler<MatchEnded>
{
    private readonly IMvpVotingRepository _votingRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenMvpVotingOnMatchEnded> _logger;

    public OpenMvpVotingOnMatchEnded(
        IMvpVotingRepository votingRepository,
        TimeProvider timeProvider,
        ILogger<OpenMvpVotingOnMatchEnded> logger)
    {
        _votingRepository = votingRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task HandleAsync(MatchEnded @event, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(@event);

        if (await _votingRepository.FindByMatchAsync(@event.MatchId, cancellationToken) is not null)
        {
            return;
        }

        var now = _timeProvider.GetUtcNow();
        try
        {
            await _votingRepository.AddAsync(MvpVoting.Open(@event.MatchId, now.AddHours(24), now), cancellationToken);
            _logger.LogInformation("MVP voting opened automatically for MatchId={MatchId}.", @event.MatchId);
        }
        catch (DbUpdateException)
        {
            // Someone opened it in the meantime (UNIQUE match_id): nothing left to do.
        }
    }
}
