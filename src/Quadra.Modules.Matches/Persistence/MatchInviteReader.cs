using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Read-only EF query adapter that implements <see cref="IMatchInviteReader"/> over the
/// <c>matches</c> and <c>match_presences</c> tables.
/// </summary>
public sealed class MatchInviteReader : IMatchInviteReader
{
    private readonly MatchesDbContext _context;
    private readonly TimeProvider _timeProvider;

    public MatchInviteReader(MatchesDbContext context, TimeProvider timeProvider)
    {
        _context = context;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc/>
    public async Task<MatchInvite?> FindInvitableAsync(
        Guid matchId,
        Guid playerId,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow();
        return await _context.Matches
            .AsNoTracking()
            .Where(m => m.Id == matchId
                && m.Visibility == MatchVisibility.Open
                && (m.Status == MatchStatus.Draft || m.Status == MatchStatus.Open)
                && m.DateTime > now
                && (m.OrganizerId == playerId
                    || _context.Presences.Any(p =>
                        p.MatchId == matchId
                        && p.PlayerId == playerId
                        && p.Status == PresenceStatus.Confirmed)))
            .Select(m => new MatchInvite(m.Id, m.Name, m.DateTime, m.OrganizerId))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
