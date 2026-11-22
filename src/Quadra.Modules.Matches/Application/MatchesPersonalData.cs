using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Contracts;
using Quadra.Modules.Matches.Entities;
using Quadra.Modules.Matches.Persistence;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Application;

/// <summary>
/// Matches part of account export and deletion (SEC-14). Deletion cancels the matches the person
/// organizes that have not started (like the organizer would), and takes them off the presence
/// list and waiting list of every match not yet played, declining first so the next person on the
/// waiting list gets the freed spot exactly as when a player declines. Played matches keep their
/// rows: they hold only the account id, and the other players' history stays whole; the cover photo
/// of the ones they organized is dropped, because the file goes with the account.
/// </summary>
public sealed class MatchesPersonalData : IPersonalDataStore
{
    private readonly MatchesDbContext _db;
    private readonly CancelMatchHandler _cancel;
    private readonly UpdateMyPresenceHandler _presence;

    public MatchesPersonalData(MatchesDbContext db, CancelMatchHandler cancel, UpdateMyPresenceHandler presence)
    {
        _db = db;
        _cancel = cancel;
        _presence = presence;
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
                // A deleted organizer's recurring groups end with them.
                await _cancel.HandleAsync(matchId, userId, endGroup: true, cancellationToken);
            }
            catch (InvalidMatchStatusTransitionException)
            {
                // The game is under way: it ends normally.
            }
        }

        // Off the waiting lists first, so the person is never the one promoted below.
        await _db.WaitingList.Where(w => w.PlayerId == userId).ExecuteDeleteAsync(cancellationToken);

        // Decline every spot still held in a match not played yet: a Regular who declines hands the
        // spot to the first Regular waiting (told by notification), the same path as tapping
        // "não vou". Outside the confirmation window the decline is refused and the spot is simply
        // freed, as it would be for anyone.
        var held = await _db.Presences.AsNoTracking()
            .Where(p => p.PlayerId == userId
                && p.Status != PresenceStatus.Declined
                && _db.Matches.Any(m => m.Id == p.MatchId
                    && (m.Status == MatchStatus.Open || m.Status == MatchStatus.Closed)))
            .Select(p => p.MatchId)
            .ToListAsync(cancellationToken);
        foreach (var matchId in held)
        {
            try
            {
                await _presence.HandleAsync(matchId, userId, new UpdateMyPresenceRequest(nameof(PresenceStatus.Declined)), cancellationToken);
            }
            catch (PresenceWindowNotOpenException)
            {
                // Window closed or not open yet: nothing to hand over.
            }
        }

        await _db.Presences
            .Where(p => p.PlayerId == userId
                && _db.Matches.Any(m => m.Id == p.MatchId && m.Status != MatchStatus.Ended))
            .ExecuteDeleteAsync(cancellationToken);

        // The cover file is deleted with the person's photo folder.
        await _db.Matches
            .Where(m => m.OrganizerId == userId && m.CoverObjectKey != null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.CoverObjectKey, (string?)null), cancellationToken);
    }
}
