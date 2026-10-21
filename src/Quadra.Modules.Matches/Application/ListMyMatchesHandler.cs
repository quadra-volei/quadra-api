using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Lists the caller's upcoming matches (GET /api/v1/matches/mine): the ones they organize, are
/// on the presence list of (unless they declined), or wait for — not cancelled, not ended,
/// soonest first. A match whose game already ended leaves the list at once, without waiting for
/// the summary (MVP voting can stay open for 24 hours).
/// </summary>
public sealed class ListMyMatchesHandler
{
    /// <summary>A match stays "upcoming" for this long after its start, so one in progress still shows.</summary>
    public static readonly TimeSpan StartGracePeriod = TimeSpan.FromHours(6);

    private const int MaxPhotosPerMatch = 4;

    private readonly IMatchRepository _matchRepository;
    private readonly IPresenceRepository _presenceRepository;
    private readonly IMatchGuestRepository _guestRepository;
    private readonly IPlayerSummaryReader _playerSummaryReader;
    private readonly TimeProvider _timeProvider;
    private readonly IMatchCoverReader _coverReader;
    private readonly IMatchResultReader _resultReader;

    public ListMyMatchesHandler(
        IMatchRepository matchRepository,
        IPresenceRepository presenceRepository,
        IMatchGuestRepository guestRepository,
        IPlayerSummaryReader playerSummaryReader,
        TimeProvider timeProvider,
        IMatchCoverReader coverReader,
        IMatchResultReader resultReader)
    {
        _coverReader = coverReader;
        _resultReader = resultReader;
        _matchRepository = matchRepository;
        _presenceRepository = presenceRepository;
        _guestRepository = guestRepository;
        _playerSummaryReader = playerSummaryReader;
        _timeProvider = timeProvider;
    }

    public async Task<MyMatchesResponse> HandleAsync(Guid callerId, CancellationToken cancellationToken)
    {
        var since = _timeProvider.GetUtcNow() - StartGracePeriod;
        var listed = await _matchRepository.ListUpcomingForPlayerAsync(callerId, since, cancellationToken);
        var ended = listed.Count == 0
            ? new HashSet<Guid>()
            : await _resultReader.ListEndedAsync(listed.Select(m => m.Id).ToArray(), cancellationToken);
        var matches = listed.Where(m => !ended.Contains(m.Id)).ToList();
        if (matches.Count == 0)
        {
            return new MyMatchesResponse([]);
        }

        var matchIds = matches.Select(m => m.Id).ToArray();
        var presences = await _presenceRepository.ListByMatchesAsync(matchIds, cancellationToken);
        var guestCounts = await _guestRepository.CountByMatchesAsync(matchIds, cancellationToken);
        var covers = await _coverReader.GetCoverUrlsAsync(matchIds, cancellationToken);

        var confirmedByMatch = presences
            .Where(p => p.Status == PresenceStatus.Confirmed)
            .GroupBy(p => p.MatchId)
            .ToDictionary(g => g.Key, g => g.OrderBy(p => p.ConfirmedAt).Select(p => p.PlayerId).ToList());

        var summaries = await _playerSummaryReader.GetSummariesAsync(
            confirmedByMatch.Values.SelectMany(ids => ids).Distinct().ToArray(),
            cancellationToken);

        var items = matches.Select(match =>
        {
            var confirmedIds = confirmedByMatch.GetValueOrDefault(match.Id) ?? [];
            var confirmedCount = confirmedIds.Count + guestCounts.GetValueOrDefault(match.Id);
            var mine = presences.FirstOrDefault(p => p.MatchId == match.Id && p.PlayerId == callerId);

            return new MyMatchResponse(
                Match: MatchMapper.ToResponse(match) with { CoverUrl = covers.GetValueOrDefault(match.Id) },
                ConfirmedCount: confirmedCount,
                OpenSlots: Math.Max(0, match.MaxPlayers - confirmedCount),
                ConfirmedPhotoUrls: confirmedIds
                    .Select(id => summaries[id].PhotoUrl)
                    .OfType<string>()
                    .Take(MaxPhotosPerMatch)
                    .ToList(),
                IsOrganizer: match.OrganizerId == callerId,
                MyStatus: mine?.Status.ToString());
        }).ToList();

        return new MyMatchesResponse(items);
    }
}
