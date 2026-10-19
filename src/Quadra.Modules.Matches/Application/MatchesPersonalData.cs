using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Matches part of account export and deletion (SEC-14). Deletion cancels the matches the person
/// organizes that have not started (like the organizer would), and takes them off the presence
/// list and waiting list of every match not yet played. Played matches keep their rows: they hold
/// only the account id, and the other players' history stays whole.
/// </summary>
public sealed class MatchesPersonalData : IPersonalDataStore
{
    private readonly MatchesDbContext _db;
    private readonly CancelMatchHandler _cancel;

    public MatchesPersonalData(MatchesDbContext db, CancelMatchHandler cancel)
    {
        _db = db;
        _cancel = cancel;
    }

    public string Section => "matches";

    public async Task<object> ExportAsync(Guid userId, CancellationToken cancellationToken) => new
    {
        Organized = await _db.Matches.AsNoTracking().Where(m => m.OrganizerId == userId)
            .Select(m => new { m.Id, m.Name, m.Address, m.DateTime, Status = m.Status.ToString(), m.CreatedAt })
            .ToListAsync(cancellationToken),
        Presences = await _db.Presences.AsNoTracking().Where(p => p.PlayerId == userId).ToListAsync(cancellationToken),
        WaitingList = await _db.WaitingList.AsNoTracking().Where(w => w.PlayerId == userId).ToListAsync(cancellationToken),
        GuestsAdded = await _db.Guests.AsNoTracking().Where(g => g.AddedBy == userId).ToListAsync(cancellationToken),
    };

    // ponytail: a confirmed spot freed here does not promote the waiting list; the next
    // confirmation or decline on that match fills it as usual.
    public async Task EraseAsync(Guid userId, CancellationToken cancellationToken)
    {
        var upcoming = await _db.Matches.AsNoTracking()
            .Where(m => m.OrganizerId == userId
                && m.Status != MatchStatus.Ended
                && m.Status != MatchStatus.Cancelled)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        foreach (var matchId in upcoming)
        {
            try
            {
                await _cancel.HandleAsync(matchId, userId, cancellationToken);
            }
            catch (InvalidMatchStatusTransitionException)
            {
                // The game is under way: it ends normally.
            }
        }

        await _db.WaitingList.Where(w => w.PlayerId == userId).ExecuteDeleteAsync(cancellationToken);
        await _db.Presences
            .Where(p => p.PlayerId == userId
                && _db.Matches.Any(m => m.Id == p.MatchId && m.Status != MatchStatus.Ended))
            .ExecuteDeleteAsync(cancellationToken);
    }
}
