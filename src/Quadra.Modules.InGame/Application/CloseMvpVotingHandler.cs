using Microsoft.Extensions.Logging;
using Quadra.Infrastructure.Messaging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;
using Quadra.Shared.Events.InGame;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Closes an open MVP voting session and selects the MVP from the tallied votes. Only the match
/// organizer may close voting via the HTTP path. Publishes <see cref="MvpAwarded"/> when an MVP
/// is decided (at least one vote).
/// </summary>
public sealed class CloseMvpVotingHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IMvpVotingRepository _votingRepository;
    private readonly IEventPublisher _eventPublisher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CloseMvpVotingHandler> _logger;

    public CloseMvpVotingHandler(
        IMatchReader matchReader,
        IMvpVotingRepository votingRepository,
        IEventPublisher eventPublisher,
        TimeProvider timeProvider,
        ILogger<CloseMvpVotingHandler> logger)
    {
        _matchReader = matchReader;
        _votingRepository = votingRepository;
        _eventPublisher = eventPublisher;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MvpVotingResponse> HandleAsync(
        Guid matchId,
        Guid callerId,
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

        // 3. Load voting (with votes).
        var voting = await _votingRepository.FindByMatchAsync(matchId, cancellationToken)
            ?? throw new MvpVotingNotFoundException();

        // 4. Voting must still be open.
        if (voting.State != MvpVotingState.Open)
        {
            throw new InvalidMvpVotingStateException("Voting is already closed.");
        }

        var now = _timeProvider.GetUtcNow();

        // 5. Tally votes and select the winner deterministically.
        var tallies = GetMvpVotingHandler.BuildTallies(voting);
        var (mvpPlayerId, mvpVoteCount) = MvpSelectionRules.SelectWinner(tallies);

        // 6. Close the session.
        voting.Close(mvpPlayerId, mvpVoteCount, now);

        // 7. Persist.
        await _votingRepository.UpdateAsync(voting, cancellationToken);

        _logger.LogInformation(
            "MVP voting closed for MatchId={MatchId}. MvpPlayerId={MvpPlayerId}, "
            + "MvpVoteCount={MvpVoteCount}, TotalVotes={TotalVotes}.",
            matchId, mvpPlayerId, mvpVoteCount, voting.TotalVotes);

        // 8. Publish MvpAwarded after commit, only when an MVP was decided.
        if (mvpPlayerId is not null)
        {
            await _eventPublisher.PublishAsync(
                new MvpAwarded(
                    MatchId: matchId,
                    PlayerId: mvpPlayerId.Value,
                    VoteCount: mvpVoteCount ?? 0,
                    TotalVotes: voting.TotalVotes,
                    AwardedAt: now,
                    OccurredAt: now),
                cancellationToken);
        }

        // 9. Map and return.
        var callerHasVoted = voting.Votes.Any(v => v.VoterPlayerId == callerId);
        return GetMvpVotingHandler.MapToResponse(voting, callerHasVoted);
    }
}
