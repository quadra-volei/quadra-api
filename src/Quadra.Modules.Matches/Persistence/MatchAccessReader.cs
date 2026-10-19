using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>EF implementation of the CanViewMatch rule (<see cref="IMatchAccessReader"/>).</summary>
public sealed class MatchAccessReader : IMatchAccessReader
{
    private readonly MatchesDbContext _context;

    public MatchAccessReader(MatchesDbContext context)
    {
        _context = context;
    }

    public Task<bool> CanViewAsync(Guid matchId, Guid callerId, CancellationToken cancellationToken) =>
        _context.VisibleTo(callerId).AnyAsync(m => m.Id == matchId, cancellationToken);

    public Task<bool> IsValidInviteAsync(Guid matchId, string inviteCode, CancellationToken cancellationToken)
    {
        var code = inviteCode.Trim().ToUpperInvariant();
        return _context.Matches.AsNoTracking().AnyAsync(
            m => m.Id == matchId && m.InviteMode == MatchInviteMode.Code && m.InviteCode == code,
            cancellationToken);
    }

    public async Task<IReadOnlySet<Guid>> ListViewableAsync(
        IReadOnlyCollection<Guid> matchIds,
        Guid callerId,
        CancellationToken cancellationToken)
    {
        if (matchIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await _context.VisibleTo(callerId)
            .Where(m => matchIds.Contains(m.Id))
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    public async Task<IReadOnlySet<Guid>> ListPublicAsync(
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken)
    {
        if (matchIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var ids = await _context.Matches.AsNoTracking()
            .Where(m => matchIds.Contains(m.Id) && m.Visibility == MatchVisibility.Open)
            .Select(m => m.Id)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }
}

internal static class MatchVisibilityQuery
{
    /// <summary>
    /// The matches <paramref name="callerId"/> may see: open, organized by them, or one they take
    /// part in (presence in any status, waiting list, or a guest they added).
    /// </summary>
    public static IQueryable<Match> VisibleTo(this MatchesDbContext context, Guid callerId) =>
        context.Matches.AsNoTracking().Where(m =>
            m.Visibility == MatchVisibility.Open
            || m.OrganizerId == callerId
            || context.Presences.Any(p => p.MatchId == m.Id && p.PlayerId == callerId)
            || context.WaitingList.Any(w => w.MatchId == m.Id && w.PlayerId == callerId)
            || context.Guests.Any(g => g.MatchId == m.Id && g.AddedBy == callerId));
}
