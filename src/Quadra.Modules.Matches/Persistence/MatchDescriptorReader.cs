using Microsoft.EntityFrameworkCore;
using Quadra.Shared.Contracts;

namespace Quadra.Modules.Matches.Persistence;

/// <summary>
/// Read-only EF query adapter that implements <see cref="IMatchDescriptorReader"/> over the
/// <c>matches</c> table. Returns only the minimal <see cref="MatchDescriptor"/> projection
/// (name + scheduled time) needed by cross-module consumers.
/// </summary>
public sealed class MatchDescriptorReader : IMatchDescriptorReader
{
    private readonly MatchesDbContext _context;

    public MatchDescriptorReader(MatchesDbContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public async Task<MatchDescriptor?> GetMatchDescriptorAsync(
        Guid matchId,
        CancellationToken cancellationToken)
    {
        return await _context.Matches
            .AsNoTracking()
            .Where(m => m.Id == matchId)
            .Select(m => new MatchDescriptor(m.Id, m.Name, m.DateTime, m.Format))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
