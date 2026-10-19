using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Infrastructure.Messaging;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.InGame;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Opens the MVP voting session for an ended match. Only the match organizer may open voting.
/// </summary>
public sealed class OpenMvpVotingHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IScoreboardRepository _scoreboardRepository;
    private readonly IMvpVotingRepository _votingRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OpenMvpVotingHandler> _logger;
    private readonly IEventPublisher? _eventPublisher;

    public OpenMvpVotingHandler(
        IMatchReader matchReader,
        IScoreboardRepository scoreboardRepository,
        IMvpVotingRepository votingRepository,
        TimeProvider timeProvider,
        ILogger<OpenMvpVotingHandler> logger,
        IEventPublisher? eventPublisher = null)
    {
        _matchReader = matchReader;
        _scoreboardRepository = scoreboardRepository;
        _votingRepository = votingRepository;
        _timeProvider = timeProvider;
        _logger = logger;
        _eventPublisher = eventPublisher;
    }

    public async Task<MvpVotingResponse> HandleAsync(
        Guid matchId,
        Guid callerId,
        DateTimeOffset? requestedDeadline,
        CancellationToken cancellationToken)
    {
        // 1. Verify match exists.
        var matchSummary = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // 2. Verify caller is organizer.
        if (matchSummary.OrganizerId != callerId)
        {
            throw new MatchAccessDeniedException(matchId, callerId);
        }

        // 3. Scoreboard must exist and be Ended (missing or not-Ended => 409, per behavior step 4).
        var scoreboard = await _scoreboardRepository.FindByMatchAsync(matchId, cancellationToken);
        if (scoreboard is null || scoreboard.State != ScoreboardState.Ended)
        {
            throw new MatchNotEndedException();
        }

        // 4. Reject if a voting session already exists for this match.
        var existing = await _votingRepository.FindByMatchAsync(matchId, cancellationToken);
        if (existing is not null)
        {
            throw new MvpVotingAlreadyExistsException();
        }

        // 5. Compute the deadline (defaults to opened_at + 24h).
        var now = _timeProvider.GetUtcNow();
        var deadlineAt = requestedDeadline ?? now.AddHours(24);

        // 6. Open the aggregate.
        var voting = MvpVoting.Open(matchId, deadlineAt, now);

        // 7. Persist. The UNIQUE (match_id) constraint is a last-resort guard against races.
        try
        {
            await _votingRepository.AddAsync(voting, cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new MvpVotingAlreadyExistsException();
        }

        _logger.LogInformation(
            "MVP voting opened for MatchId={MatchId}. DeadlineAt={DeadlineAt}.",
            matchId, deadlineAt);

        // 8. Tell the players the vote is open (push and in-app notification).
        if (_eventPublisher is not null)
        {
            await _eventPublisher.PublishAsync(new MvpVotingOpened(matchId, deadlineAt, now), cancellationToken);
        }

        // 9. Map and return.
        return GetMvpVotingHandler.MapToResponse(voting, callerHasVoted: false);
    }
}
