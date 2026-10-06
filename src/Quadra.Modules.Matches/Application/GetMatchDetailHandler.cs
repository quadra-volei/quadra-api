using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Builds the match screen payload (GET /api/v1/matches/{id}/detail): the match, the organizer,
/// the roster with player identities (asked from Profile through
/// <see cref="IPlayerSummaryReader"/>), guests, the waiting list and the caller's own standing.
/// </summary>
public sealed class GetMatchDetailHandler
{
    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly IWaitingListRepository _waitingListRepository;
    private readonly IMatchGuestRepository _guestRepository;
    private readonly IPlayerSummaryReader _playerSummaryReader;
    private readonly MatchWindowSynchronizer _windowSynchronizer;
    private readonly IMatchResultReader _matchResultReader;
    private readonly IMatchSummaryRepository _summaryRepository;

    public GetMatchDetailHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IWaitingListRepository waitingListRepository,
        IMatchGuestRepository guestRepository,
        IPlayerSummaryReader playerSummaryReader,
        MatchWindowSynchronizer windowSynchronizer,
        IMatchResultReader matchResultReader,
        IMatchSummaryRepository summaryRepository)
    {
        _matchResultReader = matchResultReader;
        _summaryRepository = summaryRepository;
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _waitingListRepository = waitingListRepository;
        _guestRepository = guestRepository;
        _playerSummaryReader = playerSummaryReader;
        _windowSynchronizer = windowSynchronizer;
    }

    public async Task<MatchDetailResponse> HandleAsync(
        Guid matchId,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        var match = await _matchRepository.FindByIdAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        match = await _windowSynchronizer.SyncAsync(match, cancellationToken);

        var presences = await _presenceRepository.ListByMatchAsync(matchId, cancellationToken);
        var waitingList = await _waitingListRepository.ListByMatchAsync(matchId, cancellationToken);
        var guests = await _guestRepository.ListByMatchAsync(matchId, cancellationToken);

        var playerIds = presences.Select(p => p.PlayerId)
            .Concat(waitingList.Select(w => w.PlayerId))
            .Append(match.OrganizerId)
            .Distinct()
            .ToArray();
        var summaries = await _playerSummaryReader.GetSummariesAsync(playerIds, cancellationToken);

        var confirmedCount = presences.Count(p => p.Status == PresenceStatus.Confirmed) + guests.Count;
        var myPresence = presences.FirstOrDefault(p => p.PlayerId == callerId);
        var myWaiting = waitingList.FirstOrDefault(w => w.PlayerId == callerId);
        var isOrganizer = match.OrganizerId == callerId;
        var game = await _matchResultReader.GetMatchResultAsync(matchId, cancellationToken);

        return new MatchDetailResponse(
            Match: MatchMapper.ToResponse(match),
            InviteCode: isOrganizer ? match.InviteCode : null,
            Organizer: MatchMapper.ToPlayer(summaries[match.OrganizerId]),
            Players: presences
                .OrderBy(p => p.ConfirmedAt ?? p.CreatedAt)
                .Select(p => new MatchRosterEntryResponse(
                    MatchMapper.ToPlayer(summaries[p.PlayerId]),
                    p.PlayerType.ToString(),
                    p.Status.ToString(),
                    p.ConfirmedAt))
                .ToList(),
            Guests: guests
                .Select(g => new MatchGuestResponse(g.Id, g.Name, g.Position, g.CreatedAt))
                .ToList(),
            WaitingList: waitingList
                .OrderBy(w => w.Position)
                .Select(w => new MatchWaitingEntryResponse(
                    MatchMapper.ToPlayer(summaries[w.PlayerId]),
                    w.PlayerType.ToString(),
                    w.Position))
                .ToList(),
            ConfirmedCount: confirmedCount,
            OpenSlots: Math.Max(0, match.MaxPlayers - confirmedCount),
            MyPresence: myPresence is null
                ? null
                : new MyPresenceResponse(myPresence.PlayerType.ToString(), myPresence.Status.ToString()),
            MyWaitingListPosition: myWaiting?.Position,
            CanJoin: myPresence is null && match.AllowsSelfEnrollment(callerId, inviteCode: null),
            Game: game is null
                ? null
                : new MatchGameResponse(
                    game.ScoreboardState,
                    game.MvpVotingState,
                    await _summaryRepository.ExistsForMatchAsync(matchId, cancellationToken)));
    }
}
