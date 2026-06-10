using Microsoft.Extensions.Logging;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Orchestrates removing a player's presence from a match (DELETE /api/v1/matches/{id}/presences/{playerId}).
/// Only the organizer may remove players, and only while the match is still in Draft.
/// </summary>
public sealed class RemovePresenceHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly ILogger<RemovePresenceHandler> _logger;

    public RemovePresenceHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        ILogger<RemovePresenceHandler> logger)
    {
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _logger = logger;
    }

    public async Task HandleAsync(
        Guid matchId,
        Guid callerId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        if (match.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        if (match.Status != MatchStatus.Draft)
        {
            throw new InvalidMatchStatusTransitionException(
                "Players can only be removed before the confirmation window opens.");
        }

        var presence = await _presenceRepository.FindByMatchAndPlayerAsync(matchId, playerId, cancellationToken)
            ?? throw new PresenceNotFoundException(matchId, playerId);

        await _presenceRepository.DeleteAsync(presence, cancellationToken);

        _logger.LogInformation(
            "Presence removed. MatchId={MatchId} PlayerId={PlayerId} PlayerType={PlayerType} Status={Status}",
            matchId, playerId, presence.PlayerType, presence.Status);
    }
}
