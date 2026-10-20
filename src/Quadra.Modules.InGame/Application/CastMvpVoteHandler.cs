using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Casts (or replaces, while Open) the caller's single MVP vote. Only who played (a team member)
/// may vote, plus the organizer even when they sat out; never for themselves.
/// </summary>
public sealed class CastMvpVoteHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IMvpVotingRepository _votingRepository;
    private readonly ITeamRepository _teamRepository;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CastMvpVoteHandler> _logger;

    public CastMvpVoteHandler(
        IMatchReader matchReader,
        IMvpVotingRepository votingRepository,
        ITeamRepository teamRepository,
        TimeProvider timeProvider,
        ILogger<CastMvpVoteHandler> logger)
    {
        _matchReader = matchReader;
        _votingRepository = votingRepository;
        _teamRepository = teamRepository;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<MvpVoteResponse> HandleAsync(
        Guid matchId,
        Guid callerId,
        Guid votedPlayerId,
        CancellationToken cancellationToken)
    {
        // 1. Verify match exists.
        var match = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // 2. Load voting session.
        var voting = await _votingRepository.FindByMatchAsync(matchId, cancellationToken)
            ?? throw new MvpVotingNotFoundException();

        // 3. Voting must be open.
        if (voting.State != MvpVotingState.Open)
        {
            throw new InvalidMvpVotingStateException("Voting is closed.");
        }

        // 4. Deadline gate (defense in depth; the Background Worker is expected to close it).
        var now = _timeProvider.GetUtcNow();
        if (now > voting.DeadlineAt)
        {
            throw new VotingDeadlinePassedException();
        }

        // 5. Build the participant set from the match's team members.
        var teams = await _teamRepository.ListByMatchAsync(matchId, cancellationToken);
        // Guests have no account: they neither vote nor can be voted for.
        var participantIds = teams
            .SelectMany(t => t.PlayerIds)
            .ToHashSet();

        // 6. Caller must have played, or be the organizer (who may vote without playing).
        if (!participantIds.Contains(callerId) && match.OrganizerId != callerId)
        {
            throw new NotAParticipantException();
        }

        // 7. Reject self-votes (primary enforcement; DB CHECK is a last-resort guard).
        if (votedPlayerId == callerId)
        {
            throw new SelfVoteNotAllowedException();
        }

        // 8. Voted player must be a participant.
        if (!participantIds.Contains(votedPlayerId))
        {
            throw new VotedPlayerNotParticipantException();
        }

        // 9. Upsert the ballot. The UNIQUE (match_id, voter_player_id) constraint guards races.
        MvpVote vote;
        try
        {
            vote = await _votingRepository.CastVoteAsync(
                voting.Id, matchId, callerId, votedPlayerId, now, cancellationToken);
        }
        catch (DbUpdateException)
        {
            throw new InvalidMvpVotingStateException(
                "Your vote could not be recorded due to a concurrent update; please retry.");
        }

        _logger.LogDebug(
            "MVP vote cast. MatchId={MatchId}, VoterPlayerId={VoterPlayerId}, VotedPlayerId={VotedPlayerId}.",
            matchId, callerId, votedPlayerId);

        // 10. Map and return.
        return new MvpVoteResponse(
            MatchId: vote.MatchId,
            VoterPlayerId: vote.VoterPlayerId,
            VotedPlayerId: vote.VotedPlayerId,
            CreatedAt: vote.CreatedAt,
            UpdatedAt: vote.UpdatedAt);
    }
}
