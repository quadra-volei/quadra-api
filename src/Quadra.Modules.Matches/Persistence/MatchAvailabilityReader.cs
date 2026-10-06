using Microsoft.EntityFrameworkCore;
using Quadra.Modules.Matches.Entities;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Read-only EF query adapter that implements <see cref="IMatchAvailabilityReader"/> over the
/// Matches-owned <c>matches</c> and <c>match_presences</c> tables. Computes per-match slot
/// occupancy so consumers (e.g. Geo) never read the presence tables directly.
/// </summary>
public sealed class MatchAvailabilityReader : IMatchAvailabilityReader
{
    private readonly MatchesDbContext _context;

    public MatchAvailabilityReader(MatchesDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyDictionary<Guid, MatchAvailability>> GetAvailabilityAsync(
        IReadOnlyCollection<Guid> matchIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(matchIds);

        if (matchIds.Count == 0)
        {
            return new Dictionary<Guid, MatchAvailability>();
        }

        var ids = matchIds.ToArray();

        var matches = await _context.Matches
            .AsNoTracking()
            .Where(m => ids.Contains(m.Id))
            .Select(m => new
            {
                m.Id,
                m.MaxPlayers,
                m.DropInSlots,
                m.Status,
                m.ReleasedDropInSlots,
            })
            .ToListAsync(cancellationToken);

        if (matches.Count == 0)
        {
            return new Dictionary<Guid, MatchAvailability>();
        }

        var confirmedCounts = await _context.Presences
            .AsNoTracking()
            .Where(p => ids.Contains(p.MatchId) && p.Status == PresenceStatus.Confirmed)
            .GroupBy(p => new { p.MatchId, p.PlayerType })
            .Select(g => new { g.Key.MatchId, g.Key.PlayerType, Count = g.Count() })
            .ToListAsync(cancellationToken);

        var confirmedByMatch = confirmedCounts
            .GroupBy(c => c.MatchId)
            .ToDictionary(
                g => g.Key,
                g => (
                    Regular: g.Where(c => c.PlayerType == PlayerType.Regular).Sum(c => c.Count),
                    DropIn: g.Where(c => c.PlayerType == PlayerType.DropIn).Sum(c => c.Count)));

        // Guests (players without an account) occupy slots like confirmed players do.
        var guestCounts = await _context.Guests
            .AsNoTracking()
            .Where(g => ids.Contains(g.MatchId))
            .GroupBy(g => g.MatchId)
            .Select(g => new { MatchId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.MatchId, g => g.Count, cancellationToken);

        var result = new Dictionary<Guid, MatchAvailability>(matches.Count);

        foreach (var match in matches)
        {
            var counts = confirmedByMatch.TryGetValue(match.Id, out var c)
                ? c
                : (Regular: 0, DropIn: 0);

            var confirmedCount = counts.Regular + counts.DropIn + guestCounts.GetValueOrDefault(match.Id);
            var openSlots = Math.Max(0, match.MaxPlayers - confirmedCount);

            // DropIn capacity: base drop-in slots plus (once the window is Closed) any released
            // Regular slots, minus confirmed DropIn presences.
            var releasedDropInSlots = match.Status == MatchStatus.Closed
                ? match.ReleasedDropInSlots ?? 0
                : 0;
            var dropInCapacity = match.DropInSlots + releasedDropInSlots;
            var hasOpenDropInSlot = counts.DropIn < dropInCapacity;

            result[match.Id] = new MatchAvailability(
                MatchId: match.Id,
                MaxPlayers: match.MaxPlayers,
                ConfirmedCount: confirmedCount,
                OpenSlots: openSlots,
                HasOpenDropInSlot: hasOpenDropInSlot);
        }

        return result;
    }
}
