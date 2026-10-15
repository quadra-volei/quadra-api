using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// The organizer edits a match (PUT /api/v1/matches/{id}) while it is Draft or Open and its
/// game has not started. Slots never drop below who already confirmed; confirmations are kept.
/// </summary>
public sealed class UpdateMatchHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly IMatchGuestRepository _guestRepository;
    private readonly IMatchResultReader _matchResultReader;
    private readonly MatchWindowSynchronizer _windowSynchronizer;
    private readonly TimeProvider _timeProvider;

    public UpdateMatchHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IMatchGuestRepository guestRepository,
        IMatchResultReader matchResultReader,
        MatchWindowSynchronizer windowSynchronizer,
        TimeProvider timeProvider)
    {
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _guestRepository = guestRepository;
        _matchResultReader = matchResultReader;
        _windowSynchronizer = windowSynchronizer;
        _timeProvider = timeProvider;
    }

    public async Task<Match> HandleAsync(
        Guid matchId,
        Guid callerId,
        UpdateMatchRequest request,
        CancellationToken cancellationToken)
    {
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        if (match.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        match = await _windowSynchronizer.SyncAsync(match, cancellationToken);
        await MatchGameGuard.EnsureNotStartedAsync(_matchResultReader, matchId, cancellationToken);

        // Guests take Regular slots (UpdateMyPresenceHandler).
        var regulars = await _presenceRepository.CountConfirmedAsync(matchId, PlayerType.Regular, cancellationToken)
            + await _guestRepository.CountByMatchAsync(matchId, cancellationToken);
        var dropIns = await _presenceRepository.CountConfirmedAsync(matchId, PlayerType.DropIn, cancellationToken);
        if (request.RegularSlots < regulars || request.MaxPlayers - request.RegularSlots < dropIns)
        {
            throw new MatchSlotsBelowConfirmedException(regulars + dropIns);
        }

        var cover = request.CoverObjectKey switch
        {
            null => match.CoverObjectKey,
            "" => null,
            var key => key,
        };

        match.Update(
            new MatchEdit(
                Name: request.Name,
                Description: request.Description,
                Address: request.Address,
                Latitude: request.Latitude,
                Longitude: request.Longitude,
                DateTime: request.DateTime,
                MaxPlayers: request.MaxPlayers,
                RegularSlots: request.RegularSlots,
                Price: request.Price,
                Format: request.Format?.ToUpperInvariant(),
                Level: request.Level is null ? null : Enum.Parse<MatchLevel>(request.Level, ignoreCase: true),
                DurationMinutes: request.DurationMinutes,
                PriceMonthly: request.PriceMonthly,
                RecurrenceDays: request.RecurrenceDays,
                CoverObjectKey: cover),
            _timeProvider.GetUtcNow());

        await _matchRepository.UpdateAsync(match, cancellationToken);
        return await _windowSynchronizer.SyncAsync(match, cancellationToken);
    }
}

/// <summary>Editing and cancelling stop once the scoreboard has started.</summary>
internal static class MatchGameGuard
{
    public static async Task EnsureNotStartedAsync(
        IMatchResultReader reader,
        Guid matchId,
        CancellationToken cancellationToken)
    {
        var game = await reader.GetMatchResultAsync(matchId, cancellationToken);
        if (game is not null && game.ScoreboardState != "NotStarted")
        {
            throw new InvalidMatchStatusTransitionException("The game of this match has already started.");
        }
    }
}
