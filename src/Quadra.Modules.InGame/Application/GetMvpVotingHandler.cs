using Quadra.Modules.InGame.Contracts;
using Quadra.Modules.InGame.Entities;
using Quadra.Modules.InGame.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Application;

/// <summary>
/// Retrieves the current MVP voting state for a match. Per-candidate tallies are revealed only
/// once voting is <see cref="MvpVotingState.Closed"/>.
/// Also hosts the shared entity-to-DTO and tally-building helpers reused by the mutating handlers.
/// </summary>
public sealed class GetMvpVotingHandler
{
    private readonly IMatchReader _matchReader;
    private readonly IMvpVotingRepository _votingRepository;

    public GetMvpVotingHandler(
        IMatchReader matchReader,
        IMvpVotingRepository votingRepository)
    {
        _matchReader = matchReader;
        _votingRepository = votingRepository;
    }

    public async Task<MvpVotingResponse> HandleAsync(
        Guid matchId,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        // 1. Verify match exists.
        _ = await _matchReader.FindMatchSummaryAsync(matchId, cancellationToken)
            ?? throw new MatchNotFoundException(matchId);

        // 2. Load voting (with votes).
        var voting = await _votingRepository.FindByMatchAsync(matchId, cancellationToken)
            ?? throw new MvpVotingNotFoundException();

        // 3. Determine whether the caller already voted.
        var callerHasVoted = voting.Votes.Any(v => v.VoterPlayerId == callerId);

        // 4. Map and return.
        return MapToResponse(voting, callerHasVoted);
    }

    /// <summary>
    /// Builds per-candidate tallies grouped by voted player, ordered by votes descending then
    /// player id ascending.
    /// </summary>
    internal static IReadOnlyList<MvpVoteTally> BuildTallies(MvpVoting voting)
    {
        return voting.Votes
            .GroupBy(v => v.VotedPlayerId)
            .Select(g => new MvpVoteTally(g.Key, g.Count()))
            .OrderByDescending(t => t.Votes)
            .ThenBy(t => t.PlayerId)
            .ToList();
    }

    /// <summary>
    /// Maps a <see cref="MvpVoting"/> aggregate to its response DTO. Results are populated only
    /// when the session is <see cref="MvpVotingState.Closed"/>.
    /// </summary>
    internal static MvpVotingResponse MapToResponse(MvpVoting voting, bool callerHasVoted)
    {
        IReadOnlyList<MvpVoteTally> results = voting.State == MvpVotingState.Closed
            ? BuildTallies(voting)
            : Array.Empty<MvpVoteTally>();

        return new MvpVotingResponse(
            Id: voting.Id,
            MatchId: voting.MatchId,
            State: voting.State.ToString(),
            DeadlineAt: voting.DeadlineAt,
            TotalVotes: voting.TotalVotes,
            CallerHasVoted: callerHasVoted,
            MvpPlayerId: voting.MvpPlayerId,
            MvpVoteCount: voting.MvpVoteCount,
            Results: results,
            OpenedAt: voting.OpenedAt,
            ClosedAt: voting.ClosedAt,
            CreatedAt: voting.CreatedAt,
            UpdatedAt: voting.UpdatedAt);
    }
}
