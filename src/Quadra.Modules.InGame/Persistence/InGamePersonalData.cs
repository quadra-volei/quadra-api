using Microsoft.EntityFrameworkCore;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.InGame.Persistence;

/// <summary>
/// InGame part of account export and deletion (SEC-14): the teams the person played in and the
/// MVP votes they cast. Deletion keeps these rows: they hold only the account id, which points at
/// nothing once the account and profile are gone, and other players' games stay whole.
/// </summary>
public sealed class InGamePersonalData : IPersonalDataStore
{
    private readonly InGameDbContext _db;

    public InGamePersonalData(InGameDbContext db)
    {
        _db = db;
    }

    public string Section => "games";

    public async Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken) => new
    {
        Teams = await _db.TeamMembers.AsNoTracking().Where(m => m.PlayerId == userId)
            .Select(m => new { m.MatchId, m.TeamId, m.CreatedAt }).ToListAsync(cancellationToken),
        MvpVotesCast = await _db.MvpVotes.AsNoTracking().Where(v => v.VoterPlayerId == userId)
            .Select(v => new { v.MatchId, v.VotedPlayerId, v.CreatedAt }).ToListAsync(cancellationToken),
        Stats = await _db.PlayerMatchStats.AsNoTracking().Where(s => s.PlayerId == userId)
            .Select(s => new { s.MatchId, s.Kills, s.Blocks, s.Aces, s.Digs, s.Assists, s.UpdatedAt }).ToListAsync(cancellationToken),
    };

    public Task EraseAsync(Guid userId, CancellationToken cancellationToken) => Task.CompletedTask;
}
