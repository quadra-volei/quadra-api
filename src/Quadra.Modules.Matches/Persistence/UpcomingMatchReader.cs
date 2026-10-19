using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>Matches about to start, with who is pending or confirmed (the "amanhã tem jogo" reminder).</summary>
public sealed class UpcomingMatchReader : IUpcomingMatchReader
{
    private readonly MatchesDbContext _context;

    public UpcomingMatchReader(MatchesDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<UpcomingMatch>> ListStartingBetweenAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken)
    {
        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        var matches = await _context.Matches.AsNoTracking()
            .Where(m => m.DateTime > fromUtc && m.DateTime <= toUtc
                && m.Status != MatchStatus.Cancelled && m.Status != MatchStatus.Ended)
            .Select(m => new { m.Id, m.Name, m.DateTime })
            .ToListAsync(cancellationToken);
        var ids = matches.Select(m => m.Id).ToArray();
        var presences = await _context.Presences.AsNoTracking()
            .Where(p => ids.Contains(p.MatchId)
                && (p.Status == PresenceStatus.Pending || p.Status == PresenceStatus.Confirmed))
            .Select(p => new { p.MatchId, p.PlayerId, p.Status })
            .ToListAsync(cancellationToken);

        return matches
            .Select(m => new UpcomingMatch(
                m.Id,
                m.Name,
                m.DateTime,
                presences.Where(p => p.MatchId == m.Id)
                    .Select(p => new UpcomingPlayer(p.PlayerId, p.Status == PresenceStatus.Confirmed))
                    .ToList()))
            .ToList();
    }
}
