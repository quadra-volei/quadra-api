using Microsoft.EntityFrameworkCore;
using Quadra.Modules.InGame.Entities;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// EF Core implementation of <see cref="IMvpVotingRepository"/>.
/// </summary>
public sealed class MvpVotingRepository : IMvpVotingRepository
{
    private readonly InGameDbContext _context;

    public MvpVotingRepository(InGameDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task AddAsync(MvpVoting voting, CancellationToken cancellationToken)
    {
        await _context.MvpVotings.AddAsync(voting, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<MvpVoting?> FindByMatchAsync(Guid matchId, CancellationToken cancellationToken)
    {
        var voting = await _context.MvpVotings
            .FirstOrDefaultAsync(v => v.MatchId == matchId, cancellationToken);

        if (voting is null)
        {
            return null;
        }

        var votes = await _context.MvpVotes
            .Where(v => v.VotingId == voting.Id)
            .ToListAsync(cancellationToken);

        voting.AttachVotes(votes);

        return voting;
    }

    /// <inheritdoc/>
    public async Task<MvpVote> CastVoteAsync(
        Guid votingId,
        Guid matchId,
        Guid voterPlayerId,
        Guid votedPlayerId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        var existingVote = await _context.MvpVotes
            .FirstOrDefaultAsync(
                v => v.MatchId == matchId && v.VoterPlayerId == voterPlayerId,
                cancellationToken);

        MvpVote vote;
        if (existingVote is null)
        {
            // New ballot: insert it and increment the cached vote count on the session.
            vote = MvpVote.Create(votingId, matchId, voterPlayerId, votedPlayerId, now);
            await _context.MvpVotes.AddAsync(vote, cancellationToken);

            var voting = await _context.MvpVotings
                .FirstAsync(v => v.Id == votingId, cancellationToken);
            voting.RegisterVoteCast();
        }
        else
        {
            // Replacement ballot: update the voted player only (total_votes unchanged).
            existingVote.ChangeVote(votedPlayerId, now);
            vote = existingVote;
        }

        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return vote;
    }

    /// <inheritdoc/>
    public async Task UpdateAsync(MvpVoting voting, CancellationToken cancellationToken)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // The voting session is change-tracked from FindByMatchAsync; its votes are read-only here.
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
