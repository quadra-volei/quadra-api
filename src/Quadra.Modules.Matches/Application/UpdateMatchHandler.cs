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
    private readonly MatchArenaResolver _arenaResolver;

    public UpdateMatchHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IMatchGuestRepository guestRepository,
        IMatchResultReader matchResultReader,
        MatchWindowSynchronizer windowSynchronizer,
        TimeProvider timeProvider,
        MatchArenaResolver arenaResolver)
    {
        _arenaResolver = arenaResolver;
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
        // Slots are checked against the confirmed players: no confirmation may slip in between.
        await using var transaction = await _matchRepository.LockAsync(matchId, cancellationToken);

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

        // Sending a place or an arena relinks the game; sending neither keeps its arena (Update
        // drops it when the game moved).
        var relink = request.ArenaId is not null || request.PlaceId is not null;
        var arenaId = relink ? await _arenaResolver.ResolveAsync(request.ArenaId, request.PlaceId, cancellationToken) : null;

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
                GamePoints: request.GamePoints,
                PriceMonthly: request.PriceMonthly,
                RecurrenceDays: request.RecurrenceDays,
                CoverObjectKey: cover,
                FreeTeams: request.FreeTeams),
            _timeProvider.GetUtcNow());
        if (relink)
        {
            match.LinkArena(arenaId, _timeProvider.GetUtcNow());
        }

        await _matchRepository.UpdateAsync(match, cancellationToken);
        match = await _windowSynchronizer.SyncAsync(match, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return match;
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
