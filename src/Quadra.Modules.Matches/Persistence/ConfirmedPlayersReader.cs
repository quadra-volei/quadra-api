using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Read-only EF query adapter that implements <see cref="IConfirmedPlayersReader"/> over
/// the <c>match_presences</c> table. Returns only the player IDs of confirmed presences.
/// </summary>
public sealed class ConfirmedPlayersReader : IConfirmedPlayersReader
{
    private readonly MatchesDbContext _context;

    public ConfirmedPlayersReader(MatchesDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<Guid>> GetConfirmedPlayerIdsAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        return await _context.Presences
            .AsNoTracking()
            .Where(p => p.MatchId == matchId && p.Status == PresenceStatus.Confirmed)
            .Select(p => p.PlayerId)
            .ToListAsync(cancellationToken);
    }
}
