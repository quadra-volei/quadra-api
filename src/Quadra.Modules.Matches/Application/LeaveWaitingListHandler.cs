using Microsoft.Extensions.Logging;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Takes the caller off a match's waiting list (DELETE /api/v1/matches/{id}/waiting-list/me).
/// A waiter has no presence row, so "Declined" on <c>presences/me</c> cannot reach them.
/// </summary>
public sealed class LeaveWaitingListHandler
{
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly ILogger<LeaveWaitingListHandler> _logger;

    public LeaveWaitingListHandler(
        IWaitingListRepository waitingListRepository,
        ILogger<LeaveWaitingListHandler> logger)
    {
        _waitingListRepository = waitingListRepository;
        _logger = logger;
    }

    /// <returns><c>false</c> when the caller is not on this match's waiting list.</returns>
    public async Task<bool> HandleAsync(Guid matchId, Guid callerId, CancellationToken cancellationToken)
    {
        var entry = await _waitingListRepository.FindByMatchAndPlayerAsync(matchId, callerId, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        await _waitingListRepository.RemoveAsync(entry, cancellationToken);

        _logger.LogInformation(
            "Left waiting list. MatchId={MatchId} PlayerId={PlayerId} Position={Position}",
            matchId, callerId, entry.Position);
        return true;
    }
}
